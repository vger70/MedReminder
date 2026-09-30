namespace MedReminder.Application.Catalogue;

// Transport of the remote catalogue feeds. The Infrastructure adapter
// reads a feed's manifest and downloads its archive over HTTPS.
public interface ICatalogueFeedClient
{
    // The manifest `feed` publishes, or null when it cannot be fetched
    // or parsed, or belongs to another feed (the adapter logs why).
    // Never throws except on cancellation of `cancellationToken`.
    Task<CatalogueFeedManifest?> GetLatestAsync(CatalogueFeedDescriptor feed, CancellationToken cancellationToken);

    // Downloads the archive the manifest names to `destinationPath`
    // (written as `<destinationPath>.part`, renamed when complete).
    // Throws on any failure; the caller deletes both files.
    Task<CatalogueFeedDownload> DownloadAsync(
        CatalogueFeedDescriptor feed,
        CatalogueFeedManifest manifest,
        string destinationPath,
        CancellationToken cancellationToken);
}

// Byte count and lowercase hexadecimal SHA-256 of a downloaded archive.
public sealed record CatalogueFeedDownload(long Length, string Sha256);
