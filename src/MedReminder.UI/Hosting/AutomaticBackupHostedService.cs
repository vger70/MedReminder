using System.Globalization;
using MedReminder.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Hosting;

// Automatic daily backup scheduler.
//
// "At-least-once-a-day with a preferred time" policy:
//   - Tick every 15 minutes.
//   - Backup if: Enabled && LastSuccessfulBackupAt.Date < today
//     (local) && currentLocalTime >= PreferredTime.
//   - ⇒ If the app starts late relative to the preferred time (e.g.
//     PC on at 09:00 but time set to 03:00), immediate catch-up on
//     the first tick.
//   - ⇒ If the app was not running at the preferred time, the
//     previous day's backup is lost; the current day's backup runs
//     at the first tick after the preferred time. No day with a
//     live app goes without a backup.
//
// Fault-tolerant: an export / prune failure does not stop the
// scheduler and is recorded in the state (LastError /
// LastAttemptAt) as well as in the logs.
internal sealed class AutomaticBackupHostedService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeOnly FallbackPreferredTime = new(3, 0);

    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<BackupSettings> _settings;
    private readonly IBackupStateStore _stateStore;
    private readonly TimeProvider _clock;
    private readonly ILogger<AutomaticBackupHostedService> _log;

    public AutomaticBackupHostedService(
        IServiceProvider services,
        IOptionsMonitor<BackupSettings> settings,
        IBackupStateStore stateStore,
        TimeProvider clock,
        ILogger<AutomaticBackupHostedService> log)
    {
        _services = services;
        _settings = settings;
        _stateStore = stateStore;
        _clock = clock;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "Backup scheduler started; tick every {Minutes} minutes.",
            TickInterval.TotalMinutes);

        // Initial delay: give DatabaseInitializer and the monitor
        // time to stabilize before opening a new connection to the
        // DB for a possible backup.
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await TryRunAsync(stoppingToken);
            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _log.LogInformation("Backup scheduler stopped.");
    }

    private async Task TryRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = _settings.CurrentValue;
            if (!settings.Enabled) return;
            if (string.IsNullOrWhiteSpace(settings.Directory))
            {
                _log.LogWarning("Backup enabled but no folder configured; skip.");
                return;
            }

            var preferred = ParsePreferredTime(settings.PreferredTime);
            var state = _stateStore.Load();
            var nowLocal = ConvertToLocal(_clock.GetUtcNow());
            var todayLocal = DateOnly.FromDateTime(nowLocal.DateTime);
            var timeOfDay = TimeOnly.FromDateTime(nowLocal.DateTime);

            if (state.LastSuccessfulBackupAt is { } last)
            {
                var lastLocalDay = DateOnly.FromDateTime(
                    ConvertToLocal(last).DateTime);
                if (lastLocalDay >= todayLocal)
                {
                    // Today's backup already done.
                    return;
                }
            }

            if (timeOfDay < preferred)
            {
                // Not yet time.
                return;
            }

            _log.LogInformation(
                "Starting automatic daily backup into {Directory}.",
                settings.Directory);

            await using var scope = _services.CreateAsyncScope();
            var backup = scope.ServiceProvider.GetRequiredService<IBackupService>();

            var file = await backup.ExportAsync(settings.Directory, cancellationToken);

            var pruned = 0;
            if (settings.RetentionDays > 0)
            {
                pruned = await backup.PruneOldBackupsAsync(
                    settings.Directory, settings.RetentionDays, cancellationToken);
            }

            _stateStore.Save(new BackupState(
                LastSuccessfulBackupAt: _clock.GetUtcNow(),
                LastAttemptAt: _clock.GetUtcNow(),
                LastError: null,
                LastBackupFile: file));

            _log.LogInformation(
                "Automatic backup completed: {File} (retention: {Pruned} files removed).",
                file, pruned);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Clean shutdown.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Automatic backup failed.");
            try
            {
                var existing = _stateStore.Load();
                _stateStore.Save(new BackupState(
                    LastSuccessfulBackupAt: existing.LastSuccessfulBackupAt,
                    LastAttemptAt: _clock.GetUtcNow(),
                    LastError: ex.Message,
                    LastBackupFile: existing.LastBackupFile));
            }
            catch
            {
                // If even the state store is broken, the log is the
                // only source of truth for this cycle.
            }
        }
    }

    private static TimeOnly ParsePreferredTime(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return FallbackPreferredTime;
        if (TimeOnly.TryParseExact(raw, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var t))
        {
            return t;
        }
        if (TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, out t))
        {
            return t;
        }
        return FallbackPreferredTime;
    }

    private DateTimeOffset ConvertToLocal(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, _clock.LocalTimeZone);
}
