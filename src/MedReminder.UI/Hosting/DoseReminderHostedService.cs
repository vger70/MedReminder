using MedReminder.Application.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Hosting;

// Scheduler for the A5 dose-time reminder: fires DoseReminderService
// once per minute inside the active profile's IHost. Mirrors the
// structure of MedicationMonitorHostedService — a thin BackgroundService
// wrapper that delegates all logic to the Application service resolved
// per-tick inside its own DI scope (ANALYSIS-A5 §4.1).
internal sealed class DoseReminderHostedService : BackgroundService
{
    private const int DefaultIntervalSeconds = 60;
    private const int MinimumIntervalSeconds = 10;

    private readonly IServiceProvider _services;
    private readonly ILogger<DoseReminderHostedService> _log;
    private readonly TimeSpan _interval;

    public DoseReminderHostedService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<DoseReminderHostedService> log)
    {
        _services = services;
        _log = log;

        var raw = configuration["DoseReminder:IntervalSeconds"];
        var configured = int.TryParse(raw, out var v) ? v : DefaultIntervalSeconds;
        if (configured < MinimumIntervalSeconds) configured = MinimumIntervalSeconds;
        _interval = TimeSpan.FromSeconds(configured);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "Dose reminder scheduler started; interval {IntervalSeconds}s.",
            _interval.TotalSeconds);

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

        _log.LogInformation("Dose reminder scheduler stopped.");
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<DoseReminderService>();
            var result = await service.RunAsync(cancellationToken);

            if (result.FiredCount > 0)
            {
                _log.LogInformation(
                    "Dose reminder tick: {Fired} reminders fired.", result.FiredCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Clean shutdown: not an error.
        }
        catch (Exception ex)
        {
            // A single tick error must not stop the scheduler.
            _log.LogError(ex, "Error in the dose reminder tick.");
        }
    }
}
