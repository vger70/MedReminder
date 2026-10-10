using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Export;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedReminder.Infrastructure.Backup;

// The outcome of one CloudBackupRun. ArchiveIds: the snapshots stored, one
// per profile backed up. Errors: one line per failed profile, without
// medicine data. SignInRequired: the provider refused the session, so the
// host asks the user to sign in again instead of retrying.
public sealed record CloudBackupResult(
    IReadOnlyList<string> ArchiveIds,
    IReadOnlyList<string> Errors,
    bool SignInRequired,
    int Pruned)
{
    public bool Succeeded => ArchiveIds.Count > 0;
}

// One scheduled cloud backup for a host that writes archives with
// ProfileArchive (Android backlog B2-03; plan §4.3): every given profile
// is exported as an automatic snapshot (C.3+ §3.6) named by
// CloudSnapshotName, uploaded to the IArchiveStorage and then pruned by
// age. The rules are the desktop's (AutomaticBackupHostedService):
// a failure on one profile does not stop the others; a session that needs
// a new sign-in, or a storage that is no longer there, stops the run,
// since every other profile would fail the same way.
//
// Retention prunes only the profiles backed up in this run, so a profile
// whose backup keeps failing keeps its older snapshots.
//
// The host decides when to run (daily, on the household master only,
// household C3), supplies the passphrase from its
// ICloudBackupPassphraseStore and zeroes it afterwards, and records the
// result. The scratch directory must be in the app's private storage;
// each archive is deleted from it after the upload.
public sealed class CloudBackupRun
{
    private readonly ProfileArchive _archive;
    private readonly TimeProvider _clock;
    private readonly ILogger<CloudBackupRun> _log;

    public CloudBackupRun(ProfileArchive archive, TimeProvider clock, ILogger<CloudBackupRun>? log = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(clock);
        _archive = archive;
        _clock = clock;
        _log = log ?? NullLogger<CloudBackupRun>.Instance;
    }

    public async Task<CloudBackupResult> RunAsync(
        IArchiveStorage storage,
        IReadOnlyList<ProfileArchiveProfile> profiles,
        char[] passphrase,
        string appVersion,
        string deviceName,
        string scratchDirectory,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(passphrase);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);

        var archiveIds = new List<string>();
        var backedUp = new List<string>();
        var errors = new List<string>();
        var signInRequired = false;

        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                archiveIds.Add(await BackUpAsync(
                    storage, profile, passphrase, appVersion, deviceName, scratchDirectory, cancellationToken));
                backedUp.Add(profile.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (CloudSignInRequiredException ex)
            {
                signInRequired = true;
                errors.Add($"cloud: {ex.Message}");
                _log.LogWarning("Cloud backup stopped: {Provider} needs a new sign-in.", ex.Provider);
                break;
            }
            catch (DirectoryNotFoundException)
            {
                errors.Add("cloud: the backup storage is not available.");
                _log.LogWarning("Cloud backup stopped: the storage is not available.");
                break;
            }
            catch (Exception ex)
            {
                // Validation and I/O messages carry no medicine data.
                errors.Add($"cloud {profile.Id}: {ex.Message}");
                _log.LogError(ex, "Cloud backup failed for profile {Profile}.", profile.Id);
            }
        }

        var pruned = 0;
        if (backedUp.Count > 0 && retentionDays > 0)
        {
            try
            {
                pruned = await CloudSnapshots.PruneAsync(
                    storage, retentionDays, _clock.GetUtcNow(), backedUp, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The snapshots are stored; a failed prune is retried next time.
                _log.LogWarning(ex, "Cloud backup retention prune failed.");
            }
        }

        return new CloudBackupResult(archiveIds, errors, signInRequired, pruned);
    }

    private async Task<string> BackUpAsync(
        IArchiveStorage storage,
        ProfileArchiveProfile profile,
        char[] passphrase,
        string appVersion,
        string deviceName,
        string scratchDirectory,
        CancellationToken cancellationToken)
    {
        var name = CloudSnapshotName.Create(profile.Id, _clock.GetUtcNow());
        Directory.CreateDirectory(scratchDirectory);
        var path = Path.Combine(scratchDirectory, "upload-" + Guid.NewGuid().ToString("N") + ExportFormat.ArchiveExtension);
        try
        {
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await _archive.ExportAsync(profile, appVersion, file, passphrase, scratchDirectory, cancellationToken,
                    automaticDeviceName: deviceName);
            }
            // The storage disposes the stream before the file is deleted.
            var id = await storage.UploadAsync(File.OpenRead(path), name, cancellationToken);
            _log.LogInformation("Cloud backup stored {Archive} for profile {Profile}.", id, profile.Id);
            return id;
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
