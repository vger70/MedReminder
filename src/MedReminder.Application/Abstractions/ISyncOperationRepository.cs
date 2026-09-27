using MedReminder.Domain.Sync;

namespace MedReminder.Application.Abstractions;

// Local operation log (table SyncOperations, B.1 Phase 3a,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §7.3): the operations this
// device produced, written through IOperationLog in the unit of work of
// the use case, and from Phase 3b the operations applied from other
// devices, written by ApplyRemoteOperations.
public interface ISyncOperationRepository
{
    Task AddAsync(SyncOperation operation, CancellationToken cancellationToken);

    // True when an operation with this id is already in the log, local
    // or applied from another device (Phase 3b idempotency).
    Task<bool> ExistsAsync(Guid operationId, CancellationToken cancellationToken);

    // Greatest HLC in the log (null when the log is empty): the clock
    // state after a restart.
    Task<HybridTimestamp?> GetLatestTimestampAsync(CancellationToken cancellationToken);

    // In HLC order.
    Task<IReadOnlyList<SyncOperation>> ListAllAsync(CancellationToken cancellationToken);

    // The operations of one medicine, in HLC order.
    Task<IReadOnlyList<SyncOperation>> ListForMedicineAsync(Guid medicineId, CancellationToken cancellationToken);
}
