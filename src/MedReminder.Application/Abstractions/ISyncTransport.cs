namespace MedReminder.Application.Abstractions;

// Remote storage of the sync files (B.1 Phase 3c, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §5.1, §5.8). Paths are relative, with '/'
// separators, under the transport's root. Implementations: a local or
// third-party-synced folder (LocalFolderSyncTransport, Phase 3c); the
// provider APIs of Phase 4.
//
// Contract (SyncTransportContractTests):
//   - a file becomes visible to List and Read only complete: writers
//     use a temporary name that List never returns;
//   - CreateAsync never replaces a file (single writer per name, R5);
//   - Read of a missing file returns null.
public interface ISyncTransport
{
    // Every file under the prefix, recursively, as relative paths.
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken);

    Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken);

    // False when the file already exists.
    Task<bool> CreateAsync(string path, byte[] content, CancellationToken cancellationToken);

    // Creates or replaces: only for files with one writer that changes
    // them (a device's own record).
    Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken);

    Task DeleteAsync(string path, CancellationToken cancellationToken);
}
