namespace MedReminder.Application.Abstractions;

// Desktop sync setup (B.1 Phase 3d, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §5.5, §5.7): the operations that need the platform's database file
// handling. Join and rebuild replace the current profile's database, like
// an import: the current file is kept as <db>.bak-<timestamp>, and the
// caller restarts the application afterwards.
public interface ISyncSetupService
{
    // Groups found in a sync folder.
    Task<IReadOnlyList<Guid>> ListGroupsAsync(string folder, CancellationToken cancellationToken);

    // Enables sync with a new group created from this profile.
    Task CreateAsync(string folder, char[] passphrase, string deviceName, CancellationToken cancellationToken);

    // Joins a group: the profile's data is replaced by the group's.
    // Throws CryptographicException for a wrong passphrase.
    Task JoinAsync(string folder, Guid groupId, char[] passphrase, string deviceName,
        CancellationToken cancellationToken);

    // Rebuilds the profile from the group, with the stored key, after a
    // new generation or when this device was away too long.
    Task RebuildAsync(CancellationToken cancellationToken);
}
