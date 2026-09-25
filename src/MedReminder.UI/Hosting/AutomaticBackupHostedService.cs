using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
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
// one the running process opened, on both the local raw-DB target
// and the encrypted cloud-folder target. Retention runs once at the end,
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

    internal async Task TryRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = _settings.CurrentValue;
            var localEnabled = settings.Enabled && !string.IsNullOrWhiteSpace(settings.Directory);
            var cloudEnabled = settings.CloudFolderEnabled
                && !string.IsNullOrWhiteSpace(settings.CloudFolderDirectory);

            if (!localEnabled && !cloudEnabled)
            {
                if (settings.Enabled && string.IsNullOrWhiteSpace(settings.Directory))
                {
                    _log.LogWarning("Backup enabled but no folder configured; skip.");
                }
                if (settings.CloudFolderEnabled
                    && string.IsNullOrWhiteSpace(settings.CloudFolderDirectory))
                {
                    _log.LogWarning(
                        "Cloud-folder backup enabled but no folder configured; skip.");
                }
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
                "Starting automatic daily backup (local={LocalEnabled}, cloud={CloudEnabled}).",
                localEnabled, cloudEnabled);

            await using var scope = _services.CreateAsyncScope();
            var backup = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var registry = scope.ServiceProvider.GetRequiredService<IProfileRegistry>();
            var archiveStorage = scope.ServiceProvider.GetRequiredService<IArchiveStorage>();

            var profiles = registry.ListProfiles();
            if (profiles.Count == 0)
            {
                _log.LogInformation(
                    "Automatic backup skipped: no profile in the registry.");
                return;
            }

            // Every backup written this tick, by either target. The day
            // counts as backed up as soon as one exists, so a cloud-only
            // configuration does not re-export on every tick.
            var exportedFiles = new List<string>();
            var perProfileErrors = new List<string>();

            // Local raw-DB target: unchanged behaviour, per-profile
            // isolation, best-effort.
            if (localEnabled)
            {
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
            }

            // C.3+ cloud target: every profile, with an independent
            // try/catch per profile so a failure on the cloud path does
            // not skip the local one and vice versa (§4.1).
            if (cloudEnabled)
            {
                try
                {
                    exportedFiles.AddRange(await RunCloudTargetAsync(
                        scope.ServiceProvider,
                        archiveStorage,
                        settings.CloudFolderDirectory,
                        profiles,
                        perProfileErrors,
                        cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    perProfileErrors.Add($"cloud: {ex.Message}");
                    _log.LogError(ex, "Automatic cloud-folder backup failed.");
                }
            }

            var pruned = 0;
            if (localEnabled && settings.RetentionDays > 0)
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

            if (cloudEnabled && settings.CloudFolderRetention > 0)
            {
                try
                {
                    await backup.PruneCloudFolderAsync(
                        archiveStorage,
                        settings.CloudFolderRetention,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Cloud-folder retention prune failed.");
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
                    "Automatic backup tick completed: {ExportedCount} backup file(s) written, {Pruned} old file(s) pruned.",
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

    // C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §4.1):
    // export every registered profile to a fresh .mrz in a temp folder,
    // then hand each archive to IArchiveStorage (C.3++,
    // docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md §7.5), which owns
    // delivery into the cloud folder. All profiles are covered, like the
    // local raw-DB target; every archive is encrypted with the admin's
    // DPAPI-cached backup passphrase. A missing passphrase or folder
    // logs a warning and skips (§4.6), it is not a failure of the tick.
    // A failure on one profile is recorded in errors and does not stop
    // the others. Returns the stored archive ids.
    private async Task<IReadOnlyList<string>> RunCloudTargetAsync(
        IServiceProvider scopedServices,
        IArchiveStorage archiveStorage,
        string cloudFolderDirectory,
        IReadOnlyList<Profile> profiles,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var archiveIds = new List<string>();

        // Cheap check before the Argon2id export: a missing folder keeps
        // the day open, so without it every 15-minute tick would pay for
        // an export that the upload then rejects. Specific to the local
        // folder, the only IArchiveStorage backend so far; a native
        // provider will need its own availability check here.
        if (!Directory.Exists(cloudFolderDirectory))
        {
            _log.LogWarning(
                "Cloud-folder backup skipped: folder {Directory} does not exist.",
                cloudFolderDirectory);
            return archiveIds;
        }

        var passStore = scopedServices.GetService<ICloudBackupPassphraseStore>();
        if (passStore is null || !passStore.HasPassphrase)
        {
            _log.LogWarning(
                "Cloud-folder backup skipped: no backup passphrase configured on this machine.");
            return archiveIds;
        }

        var passphrase = passStore.GetPassphrase();
        if (passphrase is null)
        {
            _log.LogWarning(
                "Cloud-folder backup skipped: the stored backup passphrase could not be read.");
            return archiveIds;
        }

        var exportService = scopedServices.GetRequiredService<IExportService>();
        try
        {
            foreach (var profile in profiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    archiveIds.Add(await ExportProfileToCloudAsync(
                        exportService, archiveStorage, profile.Id, passphrase, cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (DirectoryNotFoundException)
                {
                    // Folder removed while the export ran: skip the rest of
                    // this run, not a failure of the tick (ANALYSIS-C3PLUS §4.3).
                    _log.LogWarning(
                        "Cloud-folder backup skipped: folder {Directory} does not exist.",
                        cloudFolderDirectory);
                    break;
                }
                catch (Exception ex)
                {
                    errors.Add($"cloud {profile.Id}: {ex.Message}");
                    _log.LogError(ex,
                        "Automatic cloud-folder backup failed for profile {Profile}.", profile.Id);
                }
            }
        }
        finally
        {
            Array.Clear(passphrase, 0, passphrase.Length);
        }

        return archiveIds;
    }

    private async Task<string> ExportProfileToCloudAsync(
        IExportService exportService,
        IArchiveStorage archiveStorage,
        string profileId,
        char[] passphrase,
        CancellationToken cancellationToken)
    {
        var scratchDirectory = Path.Combine(
            Path.GetTempPath(), "MedReminder-cloud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDirectory);

        var timestamp = _clock.GetUtcNow().ToString("yyyyMMdd-HHmmss");
        var fileName = $"medreminder-{profileId}-{timestamp}.mrz";
        var tempPath = Path.Combine(scratchDirectory, fileName);

        try
        {
            await exportService.ExportAsync(
                new ExportOptions
                {
                    DestinationPath = tempPath,
                    ProfileId = profileId,
                    Scope = ExportScope.Profile,
                    IncludeSmtpSettings = false,
                    IncludeSmtpPassword = false,
                    IncludeBackupSettings = false,
                    IncludeUserSettings = false,
                    AutomaticSource = true,
                },
                passphrase,
                progress: null,
                cancellationToken);

            // The storage disposes the stream; it must be closed before
            // the finally block deletes the temp file.
            var archiveId = await archiveStorage.UploadAsync(
                File.OpenRead(tempPath), fileName, cancellationToken);
            _log.LogInformation(
                "Cloud-folder backup wrote {File} for profile {Profile}.", archiveId, profileId);
            return archiveId;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                if (Directory.Exists(scratchDirectory))
                {
                    Directory.Delete(scratchDirectory, recursive: true);
                }
            }
            catch
            {
                // A leftover temp file in %TEMP% is harmless; do not
                // fail the tick over it.
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
