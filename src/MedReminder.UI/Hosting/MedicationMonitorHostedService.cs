using MedReminder.Application.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Hosting;

// Internal monitor scheduler (spec §18): runs a consumption catch-up
// and a notification check cycle at regular intervals.
//
// Lives in the UI project because the composition root decides when
// to start the monitoring logic — consistent with ANALYSIS §2.6.
// Every tick creates its own DI scope: EF Core stays scoped, the
// DbContext is not shared between concurrent ticks.
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

        // Use the string indexer to avoid an explicit dependency on
        // Microsoft.Extensions.Configuration.Binder (GetValue<T>).
        var raw = configuration["Monitoring:IntervalMinutes"];
        var configured = int.TryParse(raw, out var v) ? v : DefaultIntervalMinutes;
        if (configured < MinimumIntervalMinutes) configured = MinimumIntervalMinutes;
        _interval = TimeSpan.FromMinutes(configured);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "Monitor scheduler started; interval {IntervalMinutes} minutes.",
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

        _log.LogInformation("Monitor scheduler stopped.");
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
                "Monitor tick: consumptions materialized={Consumptions}, medicines inspected={Inspected}, notifications sent={Sent}",
                consumptionCreated, result.MedicinesInspected, result.NotificationsSent);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Clean shutdown: not an error.
        }
        catch (Exception ex)
        {
            // A single tick error must not stop the scheduler.
            _log.LogError(ex, "Error in the medicine monitor tick.");
        }
    }
}
