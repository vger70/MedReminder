namespace MedReminder.Application.Catalogue;

// Transport of the remote AIFA feed. The Infrastructure adapter reads
// the manifest and downloads the archive over HTTPS.
public interface ICatalogueFeedClient
{
    // The published manifest, or null when it cannot be fetched or
    // parsed (the adapter logs why). Never throws except on
    // cancellation of `cancellationToken`.
    Task<CatalogueFeedManifest?> GetLatestAsync(CancellationToken cancellationToken);

    // Downloads the archive the manifest names to `destinationPath`
    // (written as `<destinationPath>.part`, renamed when complete).
    // Throws on any failure; the caller deletes both files.
    Task<CatalogueFeedDownload> DownloadAsync(
        CatalogueFeedManifest manifest,
        string destinationPath,
        CancellationToken cancellationToken);
}

// Byte count and lowercase hexadecimal SHA-256 of a downloaded archive.
public sealed record CatalogueFeedDownload(long Length, string Sha256);
