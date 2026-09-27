using MedReminder.Domain.Sync;

namespace MedReminder.Application.Abstractions;

// Versions of the last-writer-wins registers (table SyncFieldVersions,
// B.1 Phase 3b). Written only through SyncRegisters.
public interface ISyncFieldVersionRepository
{
    Task<IReadOnlyList<SyncFieldVersion>> ListForEntityAsync(Guid entityId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncFieldVersion>> ListAllAsync(CancellationToken cancellationToken);

    Task AddAsync(SyncFieldVersion version, CancellationToken cancellationToken);
}
