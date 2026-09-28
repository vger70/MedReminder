namespace MedReminder.Application.Abstractions;

// A sync transport whose listing reaches the other devices later than its
// own writes: a provider API whose queries lag a few seconds behind writes
// (Google Drive, B.1 spike S7 C8, C17; the OneDrive delta feed), and that
// therefore lists its own creates and deletes from memory until the
// provider agrees. A folder transport lists what the file system holds and
// does not implement it.
public interface IProviderListing
{
    // True when the provider's own listing, fetched now and not completed
    // from this transport's memory of its writes, shows the path. The other
    // devices list what the provider lists.
    Task<bool> IsListedByProviderAsync(string path, CancellationToken cancellationToken);
}
