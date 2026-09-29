namespace MedReminder.Application.Export;

// Produces an encrypted .mrz archive of the current profile
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.1). User-initiated
// only — never a hosted service (§4.6). The concrete implementation
// lives in Infrastructure (ExportService).
public interface IExportService
{
    // Writes the archive described by options, encrypting the payload
    // with a key derived from passphrase. Refuses before writing any
    // file when the passphrase is shorter than
    // ExportFormat.MinPassphraseLength (§4.5), throwing
    // ExportValidationException.
    //
    // The caller owns passphrase and should zero it after the call;
    // the service does not retain it. Progress is reported at 0, 50
    // and 100 (§4.1). Returns the full path of the written archive.
    Task<string> ExportAsync(
        ExportOptions options,
        char[] passphrase,
        IProgress<int>? progress,
        CancellationToken cancellationToken);
}
