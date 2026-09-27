using MedReminder.Domain.Sync;

namespace MedReminder.Application.Abstractions;

// Local conflict list (table SyncConflicts, B.1 Phase 3b). Written only
// through SyncRegisters.
public interface ISyncConflictRepository
{
    Task<IReadOnlyList<SyncConflict>> ListForSubjectAsync(Guid subjectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncConflict>> ListAllAsync(CancellationToken cancellationToken);

    Task AddAsync(SyncConflict conflict, CancellationToken cancellationToken);

    Task RemoveAsync(SyncConflict conflict, CancellationToken cancellationToken);
}
