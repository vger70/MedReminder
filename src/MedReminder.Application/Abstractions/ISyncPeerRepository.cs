using MedReminder.Domain.Sync;

namespace MedReminder.Application.Abstractions;

// SyncPeers (B.1 Phase 3c). Written by the sync engine only.
public interface ISyncPeerRepository
{
    Task<IReadOnlyList<SyncPeer>> ListAllAsync(CancellationToken cancellationToken);

    Task AddAsync(SyncPeer peer, CancellationToken cancellationToken);

    Task UpdateAsync(SyncPeer peer, CancellationToken cancellationToken);

    // A new generation starts from an empty vector.
    Task ClearAsync(CancellationToken cancellationToken);
}
