namespace MedReminder.Application.Export;

// Reads an encrypted .mrz archive and applies it to the current
// profile in Overwrite mode
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.2). Merge is out of
// scope (§1.3). The concrete implementation lives in Infrastructure
// (ImportService).
public interface IImportService
{
    // Reads the manifest without decrypting anything, so the UI can
    // populate the confirmation panel before asking for consent (§5.3).
    // Throws ImportFailedException with
    // ImportFailureReason.UnsupportedVersion for a newer archive and
    // ImportFailureReason.Corrupt for a malformed one.
    Task<ExportManifest> ReadManifestAsync(
        string archivePath,
        CancellationToken cancellationToken);

    // Decrypts, validates and applies the archive to the current
    // profile, overwriting its data. The caller must have obtained
    // user consent first (§4.2 step 7). A safety copy of the current
    // DB is taken before the overwrite (§4.2 step 8) and the write
    // runs in a single transaction.
    //
    // The caller owns passphrase and should zero it after the call.
    // Progress is reported across the run. Throws
    // ImportFailedException on any failure surface (§4.4); on failure
    // the target profile is left untouched.
    Task ImportAsync(
        string archivePath,
        char[] passphrase,
        ImportOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken);
}
