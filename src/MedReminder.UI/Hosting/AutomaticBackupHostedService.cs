using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Hosting;

// Automatic daily backup scheduler.
//
// "At-least-once-a-day with a preferred time" policy:
//   - Tick every 15 minutes.
//   - Back up a target if: it is enabled && its last backup day
//     (LastLocalBackupAt / LastCloudBackupAt) < today (local) &&
//     currentLocalTime >= PreferredTime. The two targets keep separate
//     days, so one that failed is retried while the other is done.
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
    // A cloud target that ran without storing a snapshot (offline, storage
    // error, missing folder) waits this long before the next try: each try
    // costs an Argon2id export per profile. A pending sign-in is checked
    // on every tick, before any export.
    internal static readonly TimeSpan CloudRetryInterval = TimeSpan.FromHours(1);

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

    // True without a household (tests, or a host without the household
    // services), as before step H4a.
    private async Task<bool> SendsAsMasterAsync(CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        return scope.ServiceProvider.GetService<IMasterRole>() is not { } master
            || await master.SendsEmailAsync(cancellationToken);
    }

    internal async Task TryRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = _settings.CurrentValue;
            var localEnabled = settings.Enabled && !string.IsNullOrWhiteSpace(settings.Directory);
            var cloudEnabled = settings.CloudFolderEnabled && settings.IsCloudTargetConfigured();
            // Household step H4a (§7.2): only the master runs the cloud
            // backup; the local backup stays on every device.
            if (cloudEnabled && !await SendsAsMasterAsync(cancellationToken))
            {
                _log.LogInformation("Cloud-folder backup left to the master device.");
                cloudEnabled = false;
            }

            if (!localEnabled && !cloudEnabled)
            {
                if (settings.Enabled && string.IsNullOrWhiteSpace(settings.Directory))
                {
                    _log.LogWarning("Backup enabled but no folder configured; skip.");
                }
                if (settings.CloudFolderEnabled && !settings.IsCloudTargetConfigured())
                {
                    _log.LogWarning(
                        "Cloud-folder backup enabled but no folder or account configured; skip.");
                }
                return;
            }

            var preferred = ParsePreferredTime(settings.PreferredTime);
            var state = _stateStore.Load();
            var nowLocal = ConvertToLocal(_clock.GetUtcNow());
            var todayLocal = DateOnly.FromDateTime(nowLocal.DateTime);
            var timeOfDay = TimeOnly.FromDateTime(nowLocal.DateTime);

            // Each target keeps its own day: a cloud target that failed
            // (a disconnected account) is retried while the local one is
            // already done.
            var localDue = localEnabled && !IsToday(state.LastLocalBackupAt, todayLocal);
            var cloudDue = cloudEnabled && !IsToday(state.LastCloudBackupAt, todayLocal)
                && !(state.LastCloudFailureAt is { } failedAt
                     && _clock.GetUtcNow() - failedAt < CloudRetryInterval);
            if (!localDue && !cloudDue)
            {
                return;
            }

            if (timeOfDay < preferred)
            {
                return;
            }

            _log.LogInformation(
                "Starting automatic daily backup (local={LocalDue}, cloud={CloudDue}).",
                localDue, cloudDue);

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

            // A target's day counts as backed up as soon as one of its
            // backups exists, so a target does not re-export on every tick.
            var localFiles = new List<string>();
            var cloudArchives = new List<string>();
            var localErrors = new List<string>();
            var cloudErrors = new List<string>();
            CloudProvider? signInRequired = null;

            // Local raw-DB target: per-profile isolation, best-effort.
            if (localDue)
            {
                foreach (var profile in profiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var file = await backup.ExportProfileAsync(
                            profile.Id, settings.Directory, cancellationToken);
                        localFiles.Add(file);
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
                        localErrors.Add($"{profile.Id}: {ex.Message}");
                        _log.LogError(ex,
                            "Automatic backup failed for profile {Profile}.", profile.Id);
                    }
                }
            }

            // C.3+ cloud target: every profile, with an independent
            // try/catch per profile so a failure on the cloud path does
            // not skip the local one and vice versa (§4.1).
            if (cloudDue)
            {
                try
                {
                    var cloud = await RunCloudTargetAsync(
                        scope.ServiceProvider,
                        archiveStorage,
                        settings,
                        profiles,
                        cloudErrors,
                        cancellationToken);
                    cloudArchives.AddRange(cloud.ArchiveIds);
                    signInRequired = cloud.SignInRequired;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    cloudErrors.Add($"cloud: {ex.Message}");
                    _log.LogError(ex, "Automatic cloud-folder backup failed.");
                }
            }

            var pruned = 0;
            if (localDue && settings.RetentionDays > 0)
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

            if (cloudDue && settings.CloudFolderRetention > 0)
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

            var localError = BackupState.JoinErrors([.. localErrors]);
            var cloudError = BackupState.JoinErrors([.. cloudErrors]);
            var notifiedOn = await NotifyFailureAsync(
                state, localError is not null || cloudError is not null || signInRequired is not null,
                signInRequired, todayLocal, cancellationToken);

            // Applied to the latest state, not to the copy read before the
            // exports: Settings may have saved a manual backup or cleared
            // the sign-in meanwhile.
            var now = _clock.GetUtcNow();
            var lastFile = cloudArchives.Count > 0 ? cloudArchives[^1]
                : localFiles.Count > 0 ? localFiles[^1]
                : null;
            _stateStore.Update(current =>
            {
                var next = current with
                {
                    LastSuccessfulBackupAt = lastFile is null
                        ? current.LastSuccessfulBackupAt
                        : Latest(current.LastSuccessfulBackupAt, now),
                    LastLocalBackupAt = localFiles.Count > 0
                        ? Latest(current.LastLocalBackupAt, now)
                        : current.LastLocalBackupAt,
                    LastCloudBackupAt = cloudArchives.Count > 0
                        ? Latest(current.LastCloudBackupAt, now)
                        : current.LastCloudBackupAt,
                    LastAttemptAt = now,
                    LastBackupFile = lastFile ?? current.LastBackupFile,
                    // A target not run this tick keeps its last outcome; one
                    // that is no longer enabled has nothing left to report.
                    LocalError = localDue ? localError
                        : localEnabled ? current.LocalError
                        : null,
                    CloudError = cloudDue ? cloudError
                        : cloudEnabled ? current.CloudError
                        : null,
                    SignInRequired = cloudDue ? signInRequired
                        : cloudEnabled ? current.SignInRequired
                        : null,
                    LastCloudFailureAt = cloudDue
                        ? cloudArchives.Count == 0 && signInRequired is null ? now : null
                        : current.LastCloudFailureAt,
                    FailureNotifiedOn = notifiedOn,
                };
                return next with { LastError = next.AutomaticErrors() };
            });

            _log.LogInformation(
                "Automatic backup tick completed: {ExportedCount} backup file(s) written, {Pruned} old file(s) pruned.",
                localFiles.Count + cloudArchives.Count, pruned);
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
                var today = DateOnly.FromDateTime(ConvertToLocal(_clock.GetUtcNow()).DateTime);
                var notifiedOn = await NotifyFailureAsync(
                    existing, failed: true, signInRequired: null, today, cancellationToken);
                _stateStore.Update(current => current with
                {
                    LastAttemptAt = _clock.GetUtcNow(),
                    LastError = ex.Message,
                    FailureNotifiedOn = notifiedOn,
                });
            }
            catch
            {
                // If even the state store is broken, the log is the
                // only source of truth for this cycle.
            }
        }
    }

    private bool IsToday(DateTimeOffset? at, DateOnly todayLocal)
        => at is { } value && DateOnly.FromDateTime(ConvertToLocal(value).DateTime) >= todayLocal;

    private static DateTimeOffset Latest(DateTimeOffset? saved, DateTimeOffset now)
        => saved is { } value && value > now ? value : now;

    // C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §4.1):
    // export every registered profile to a fresh .mrz in a temp folder,
    // then hand each archive to IArchiveStorage (C.3++,
    // docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md §7.5), which owns
    // delivery into the cloud folder. All profiles are covered, like the
    // local raw-DB target; every archive is encrypted with the admin's
    // DPAPI-cached backup passphrase. A missing passphrase or folder
    // logs a warning and skips (§4.6), it is not a failure of the tick.
    // A failure on one profile is recorded in errors and does not stop
    // the others. Returns the stored archive ids, and the provider whose
    // session ended (disconnected or revoked account), if any.
    private async Task<(IReadOnlyList<string> ArchiveIds, CloudProvider? SignInRequired)> RunCloudTargetAsync(
        IServiceProvider scopedServices,
        IArchiveStorage archiveStorage,
        BackupSettings settings,
        IReadOnlyList<Profile> profiles,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var archiveIds = new List<string>();

        // Cheap check before the Argon2id export: a missing folder (or a
        // OneDrive session that needs a new sign-in) keeps the day open,
        // so without it every 15-minute tick would pay for an export that
        // the upload then rejects. The OneDrive check acquires a token
        // silently, so an expired or revoked session is caught here, not
        // only an account missing from the cache.
        var cloudFolderDirectory = settings.CloudFolderDirectory;
        if (settings.CloudProvider is null && !Directory.Exists(cloudFolderDirectory))
        {
            _log.LogWarning(
                "Cloud-folder backup skipped: folder {Directory} does not exist.",
                cloudFolderDirectory);
            return (archiveIds, null);
        }
        if (settings.CloudProvider is { } provider)
        {
            var accounts = scopedServices.GetService<ICloudAccountService>();
            if (accounts is null)
            {
                _log.LogWarning("Cloud backup skipped: no {Provider} account service is registered.", provider);
                return (archiveIds, null);
            }
            if (!await accounts.HasSessionAsync(provider, settings.CloudAccountId, cancellationToken))
            {
                // Recorded as the tick's failure, so the user is told that
                // a new sign-in is needed instead of a silent skip.
                LogSignInRequired(provider);
                return (archiveIds, provider);
            }
        }

        var passStore = scopedServices.GetService<ICloudBackupPassphraseStore>();
        if (passStore is null || !passStore.HasPassphrase)
        {
            _log.LogWarning(
                "Cloud-folder backup skipped: no backup passphrase configured on this machine.");
            return (archiveIds, null);
        }

        var passphrase = passStore.GetPassphrase();
        if (passphrase is null)
        {
            _log.LogWarning(
                "Cloud-folder backup skipped: the stored backup passphrase could not be read.");
            return (archiveIds, null);
        }

        var exportService = scopedServices.GetRequiredService<IExportService>();
        CloudProvider? signInRequired = null;
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
                catch (CloudSignInRequiredException ex)
                {
                    // The session ended while the export ran: the other
                    // profiles would fail the same way.
                    signInRequired = ex.Provider;
                    LogSignInRequired(ex.Provider);
                    break;
                }
                catch (DirectoryNotFoundException)
                {
                    // Folder removed while the export ran: skip the rest
                    // of this run, not a failure of the tick
                    // (ANALYSIS-C3PLUS §4.3).
                    _log.LogWarning("Cloud backup skipped: the target ({Target}) is not available.",
                        settings.CloudProvider?.ToString() ?? cloudFolderDirectory);
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

        return (archiveIds, signInRequired);
    }

    private void LogSignInRequired(CloudProvider provider)
        => _log.LogWarning("Cloud backup skipped: {Provider} needs a new sign-in.", provider);

    // Tells the user, at most once a local day, that the automatic backup
    // did not complete: without it a disconnected account or a failing
    // target is visible only in Settings, where the details stay. Returns
    // the day to record as notified; a notification that could not be
    // shown is retried on the next tick.
    private async Task<DateOnly?> NotifyFailureAsync(
        BackupState previous,
        bool failed,
        CloudProvider? signInRequired,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (!failed || previous.FailureNotifiedOn == today) return previous.FailureNotifiedOn;

        var windows = _services.GetService<IWindowsNotificationService>();
        var loc = _services.GetService<ILocalizationService>();
        if (windows is null || loc is null) return previous.FailureNotifiedOn;

        var body = signInRequired is { } provider
            ? loc.Get(ProviderKeys.For("Notifications.Backup.SignInRequired", provider))
            : loc.Get("Notifications.Backup.Failed");
        try
        {
            await windows.ShowAsync(loc.Get("Notifications.Backup.Title"), body, cancellationToken);
            return today;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Backup failure notification could not be shown.");
            return previous.FailureNotifiedOn;
        }
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

        var fileName = CloudSnapshotName.Create(profileId, _clock.GetUtcNow());
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
