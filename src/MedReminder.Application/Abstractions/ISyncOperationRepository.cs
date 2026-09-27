using MedReminder.Domain.Sync;

namespace MedReminder.Application.Abstractions;

// Local operation log (table SyncOperations, B.1 Phase 3a,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §7.3). Written only through
// IOperationLog, in the unit of work of the use case that produced the
// operations.
public interface ISyncOperationRepository
{
    Task AddAsync(SyncOperation operation, CancellationToken cancellationToken);

    // Greatest HLC in the log (null when the log is empty): the clock
    // state after a restart.
    Task<HybridTimestamp?> GetLatestTimestampAsync(CancellationToken cancellationToken);

    // In HLC order.
    Task<IReadOnlyList<SyncOperation>> ListAllAsync(CancellationToken cancellationToken);
}
