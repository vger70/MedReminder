using MedReminder.Application.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Hosting;

// Scheduler interno del monitor (spec §18): esegue un catch-up del
// consumo e un ciclo di controllo notifiche a intervalli regolari.
//
// Vive nel progetto UI perché è la composition root a decidere quando
// avviare la logica di monitoring — coerente con ANALYSIS §2.6.
// Ogni tick crea la propria scope DI: EF Core resta scoped, il
// DbContext non viene condiviso tra tick concorrenti.
internal sealed class MedicationMonitorHostedService : BackgroundService
{
    private const int DefaultIntervalMinutes = 30;
    private const int MinimumIntervalMinutes = 1;

    private readonly IServiceProvider _services;
    private readonly ILogger<MedicationMonitorHostedService> _log;
    private readonly TimeSpan _interval;

    public MedicationMonitorHostedService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<MedicationMonitorHostedService> log)
    {
        _services = services;
        _log = log;

        // Uso l'indexer stringa per evitare una dipendenza esplicita da
        // Microsoft.Extensions.Configuration.Binder (GetValue<T>).
        var raw = configuration["Monitoring:IntervalMinutes"];
        var configured = int.TryParse(raw, out var v) ? v : DefaultIntervalMinutes;
        if (configured < MinimumIntervalMinutes) configured = MinimumIntervalMinutes;
        _interval = TimeSpan.FromMinutes(configured);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "Monitor scheduler avviato; intervallo {IntervalMinutes} minuti.",
            _interval.TotalMinutes);

        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunOnceAsync(stoppingToken);
        }

        _log.LogInformation("Monitor scheduler fermato.");
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var catchUp = scope.ServiceProvider.GetRequiredService<ConsumptionCatchUp>();
            var monitor = scope.ServiceProvider.GetRequiredService<MedicationMonitor>();

            var consumptionCreated = await catchUp.RunAsync(cancellationToken);
            var result = await monitor.RunAsync(cancellationToken);

            _log.LogInformation(
                "Tick monitor: consumi materializzati={Consumptions}, medicine ispezionate={Inspected}, notifiche inviate={Sent}",
                consumptionCreated, result.MedicinesInspected, result.NotificationsSent);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutdown pulito: non è un errore.
        }
        catch (Exception ex)
        {
            // Un errore in un tick non deve fermare lo scheduler.
            _log.LogError(ex, "Errore nel tick del monitor medicine.");
        }
    }
}
