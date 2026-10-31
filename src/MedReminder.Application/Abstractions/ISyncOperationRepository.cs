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

    // Household step H3c: the operations of one type, in HLC order.
    Task<IReadOnlyList<SyncOperation>> ListOfTypeAsync(string type, CancellationToken cancellationToken);

    // The operations of one medicine, in HLC order.
    Task<IReadOnlyList<SyncOperation>> ListForMedicineAsync(Guid medicineId, CancellationToken cancellationToken);

    // Whether the log holds an operation of this type for the medicine:
    // the sync apply asks it once per medicine (MedicineDeleted) and needs
    // no payload (issue #328).
    async Task<bool> ExistsForMedicineAsync(Guid medicineId, string type, CancellationToken cancellationToken)
        => (await ListForMedicineAsync(medicineId, cancellationToken)).Any(o => o.Type == type);

    // The operations of one medicine without their payload, in HLC order:
    // the count re-evaluation reads only which row each one wrote, and when.
    async Task<IReadOnlyList<SyncOperationStamp>> ListStampsForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
        => [.. (await ListForMedicineAsync(medicineId, cancellationToken))
            .Select(o => new SyncOperationStamp(o.Type, o.EntityId, o.Timestamp))];

    // All operations of a device and generation, in HLC order (carry-over
    // at rekey, Phase 4c).
    Task<IReadOnlyList<SyncOperation>> ListByDeviceAndGenerationAsync(
        Guid deviceId, int generation, CancellationToken cancellationToken);

    // This device's operations of a generation not yet in a segment, in
    // HLC order (Phase 3c).
    Task<IReadOnlyList<SyncOperation>> ListPendingAsync(
        Guid deviceId, int generation, CancellationToken cancellationToken);

    Task MarkPublishedAsync(IReadOnlyList<SyncOperation> operations, int segmentSeq, CancellationToken cancellationToken);

    Task<long> CountAsync(int generation, CancellationToken cancellationToken);
}

// An operation of the log without its payload.
public sealed record SyncOperationStamp(string Type, Guid? EntityId, HybridTimestamp Timestamp);
