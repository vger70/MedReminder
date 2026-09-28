using MedReminder.Application.Sync;
using MedReminder.Application.Sync.Remote;

namespace MedReminder.Application.Abstractions;

// Desktop sync setup (B.1 Phase 3d, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §5.5, §5.7): the operations that need the platform's database file
// handling. Join and rebuild replace the current profile's database, like
// an import: the current file is kept as <db>.bak-<timestamp>, and the
// caller restarts the application afterwards.
public interface ISyncSetupService
{
    // Groups found in a sync folder or cloud account (Phase 4a).
    Task<IReadOnlyList<Guid>> ListGroupsAsync(SyncTarget target, CancellationToken cancellationToken);

    // Enables sync with a new group created from this profile.
    Task CreateAsync(SyncTarget target, char[] passphrase, string deviceName, CancellationToken cancellationToken);

    // Joins a group: the profile's data is replaced by the group's.
    // Throws CryptographicException for a wrong passphrase.
    Task JoinAsync(SyncTarget target, Guid groupId, char[] passphrase, string deviceName,
        CancellationToken cancellationToken);

    // Phase 4c: joins the group named by a pairing code shown on a
    // paired device (§6.1). Throws SyncPairingExpiredException when the
    // offer is over.
    Task JoinWithPairingAsync(SyncTarget target, SyncPairingCode code, string deviceName,
        CancellationToken cancellationToken);

    // Rebuilds the profile from the group, with the stored key, after a
    // new generation or when this device was away too long.
    Task RebuildAsync(CancellationToken cancellationToken);

    // Phase 4c: after a key rotation on another device (§6.2), takes the
    // new key from the new passphrase or a pairing code, rebuilds the
    // profile from the new generation and carries this device's own
    // operations over. Returns how many were carried and how many could
    // not be applied to the new generation.
    Task<(int Carried, int Dropped)> RekeyAsync(SyncKeySource source, CancellationToken cancellationToken);
}
