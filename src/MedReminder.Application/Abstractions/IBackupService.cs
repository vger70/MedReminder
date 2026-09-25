namespace MedReminder.Application.Abstractions;

// User-DB backup (spec §23, Increment 11; multi-profile in Increment
// 15c — docs/ANALYSIS-MULTI-USER.md §11.2).
//
// Every profile in the registry has its own SQLite database under
// %LOCALAPPDATA%\MedReminder\profiles\<id>\medreminder.db. The
// backup service targets one profile at a time so the automatic
// hosted service can loop over IProfileRegistry.ListProfiles and
// back everyone up on a single successful tick.
public interface IBackupService
{
    // Path of the database of the profile the calling process is
    // running against. Kept for backward compatibility with the
    // Increment 11 UI wiring (single-profile "run now" from the
    // SettingsDialog). Multi-profile operations use the explicit
    // ExportProfileAsync / ImportProfileAsync overloads below.
    string DatabasePath { get; }

    // Copies the specified profile's DB into destinationDirectory
    // using the SQLite online-backup API. The output file is named
    // "medreminder-<profileId>-YYYYMMDD-HHmmss.db" so different
    // profiles' backups can share a folder without collision (§11.1).
    // Returns the full path of the created file.
    Task<string> ExportProfileAsync(
        string profileId,
        string destinationDirectory,
        CancellationToken cancellationToken);

    // Removes backup files older than retentionDays. The regex is
    // extended to capture the profileId so retention is applied
    // per-profile: the most recent backup of profile A never
    // "protects" older backups of profile B (§11.1). Files that do
    // not match the expected pattern are left alone.
    Task<int> PruneOldBackupsAsync(
        string directory, int retentionDays, CancellationToken cancellationToken);

    // Replaces the DB of the specified profile with the given
    // backup file. Idempotently forces ClearAllPools() so pooled
    // SQLite connections release the target file on Windows. If
    // profileId is the currently active profile, the UI caller must
    // follow up with IApplicationRestarter.RestartAndExit() — the
    // process is holding the old file open through EF Core (§11.2).
    Task ImportProfileAsync(
        string profileId,
        string sourceFilePath,
        CancellationToken cancellationToken);

    // C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.3, §4.5):
    // retention pruning for the encrypted .mrz cloud-folder snapshots.
    // Uses a distinct regex from PruneOldBackupsAsync so the two
    // targets never cross-prune when the user configures the same
    // folder for both. Groups by profileId: the most recent .mrz of
    // profile A does not shield old .mrz files of profile B.
    Task<int> PruneCloudFolderAsync(
        string directory,
        int retentionDays,
        CancellationToken cancellationToken);
}
