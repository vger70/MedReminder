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
// Multi-profile (Increment 15c, docs/ANALYSIS-MULTI-USER.md §11.1):
// each tick backs up EVERY profile in the registry, not only the
// one the running process opened. Retention runs once at the end,
// on the shared folder, and applies per-profile (§11.1). A backup
// error on one profile does not stop the others.
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
                    return;
                }
            }

            if (timeOfDay < preferred)
            {
                return;
            }

            _log.LogInformation(
                "Starting automatic daily backup into {Directory}.",
                settings.Directory);

            await using var scope = _services.CreateAsyncScope();
            var backup = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var registry = scope.ServiceProvider.GetRequiredService<IProfileRegistry>();

            var profiles = registry.ListProfiles();
            if (profiles.Count == 0)
            {
                _log.LogInformation(
                    "Automatic backup skipped: no profile in the registry.");
                return;
            }

            // Back up every profile. A failure on one profile is
            // logged but does not stop the others: the daily backup
            // is best-effort per profile. The overall tick is
            // considered successful when at least one profile was
            // exported; that way an unrecoverable failure on one
            // profile does not silently mask days without any
            // backup at all.
            var exportedFiles = new List<string>();
            var perProfileErrors = new List<string>();
            foreach (var profile in profiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var file = await backup.ExportProfileAsync(
                        profile.Id, settings.Directory, cancellationToken);
                    exportedFiles.Add(file);
                    _log.LogInformation(
                        "Automatic backup exported profile {Profile} to {File}.",
                        profile.Id, file);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var message = $"{profile.Id}: {ex.Message}";
                    perProfileErrors.Add(message);
                    _log.LogError(ex,
                        "Automatic backup failed for profile {Profile}.", profile.Id);
                }
            }

            var pruned = 0;
            if (settings.RetentionDays > 0)
            {
                try
                {
                    pruned = await backup.PruneOldBackupsAsync(
                        settings.Directory, settings.RetentionDays, cancellationToken);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Backup retention prune failed.");
                }
            }

            var errorSummary = perProfileErrors.Count == 0
                ? null
                : string.Join("; ", perProfileErrors);

            if (exportedFiles.Count > 0)
            {
                _stateStore.Save(new BackupState(
                    LastSuccessfulBackupAt: _clock.GetUtcNow(),
                    LastAttemptAt: _clock.GetUtcNow(),
                    LastError: errorSummary,
                    LastBackupFile: exportedFiles[^1]));

                _log.LogInformation(
                    "Automatic backup tick completed: {ExportedCount} profile(s) exported, {Pruned} old file(s) pruned.",
                    exportedFiles.Count, pruned);
            }
            else
            {
                _stateStore.Save(new BackupState(
                    LastSuccessfulBackupAt: state.LastSuccessfulBackupAt,
                    LastAttemptAt: _clock.GetUtcNow(),
                    LastError: errorSummary,
                    LastBackupFile: state.LastBackupFile));
            }
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
