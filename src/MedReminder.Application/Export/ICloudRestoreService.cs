using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Export;

// Explicit restore from a C.3+ cloud-folder snapshot
// (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §4.4). Thin scheduler +
// UI wrapper over IImportService: enumerating snapshots and reading
// their manifests happens here; the actual decrypt-and-apply step
// delegates to IImportService verbatim so C.3+ has no separate
// import code path.
public interface ICloudRestoreService
{
    // Lists the .mrz snapshots in the given folder, most recent first,
    // parsing each manifest (without decrypting) so the Restore dialog
    // can render created-at / profile / hashed-hostname columns before
    // asking for the passphrase. Files whose manifest is missing or
    // malformed are skipped, not surfaced as failures — the dialog
    // shows only the archives the user can act on.
    Task<IReadOnlyList<CloudSnapshotInfo>> ListSnapshotsAsync(
        string folderPath,
        CancellationToken cancellationToken);

    // Decrypts the chosen snapshot with the given passphrase and
    // applies it to the current profile in Overwrite mode. Delegates
    // to IImportService.ImportAsync so the C.3 safety-copy / restart
    // discipline (ANALYSIS-C3 §4.2 steps 8-11) applies unchanged.
    // The caller owns passphrase and should zero it after the call.
    Task RestoreAsync(
        string archivePath,
        char[] passphrase,
        ImportOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken);

    // C.3++ Phase 2 (B.1 Phase 4a): snapshots held by a provider API
    // (IArchiveStorage), most recent first. Reading every manifest would
    // mean downloading every archive, so the profile and the date come
    // from the C.3+ file name (§3.3); device, source and version stay
    // empty. ArchivePath is the storage id.
    Task<IReadOnlyList<CloudSnapshotInfo>> ListStoredSnapshotsAsync(
        IArchiveStorage storage,
        CancellationToken cancellationToken);

    // Downloads the snapshot to a temporary file under the application
    // data folder, restores it like RestoreAsync, and deletes the file.
    Task RestoreStoredAsync(
        IArchiveStorage storage,
        string archiveId,
        char[] passphrase,
        ImportOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken);
}

// Per-snapshot metadata surfaced to the Restore dialog. Cleartext,
// safe to display before decryption: everything here comes from the
// archive's manifest.json.
public sealed record CloudSnapshotInfo
{
    public required string ArchivePath { get; init; }

    public required string FileName { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public string ProfileId { get; init; } = string.Empty;

    // C.3+ §3.6 hashed device marker. Empty when the archive was
    // produced by a user-triggered C.3 export (no device block).
    public string DeviceHostHash { get; init; } = string.Empty;

    // "automatic" for a scheduled cloud snapshot, "user" (or empty)
    // for a manual C.3 export.
    public string Source { get; init; } = string.Empty;

    public string AppVersion { get; init; } = string.Empty;
}
