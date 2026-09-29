namespace MedReminder.Domain.Sync;

// One row of the local operation log (table SyncOperations, B.1 Phase
// 3a, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §7.3): an operation this
// device produced, waiting to be published (SegmentSeq null) or already
// in a segment. From Phase 3b the table also records the ids of the
// operations applied from other devices, so a re-download is harmless.
//
// The body is stored as JSON under its catalogue type name and schema
// version (OperationCodec, Application), so a device never has to
// understand an operation to keep it.
public sealed class SyncOperation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required long HlcPhysicalMs { get; init; }

    public required int HlcCounter { get; init; }

    public required Guid DeviceId { get; init; }

    // Sync generation the operation belongs to (§5.7).
    public required int Generation { get; init; }

    public required string Type { get; init; }

    public required int SchemaVersion { get; init; }

    public required Guid MedicineId { get; init; }

    // The row the operation creates or writes (Phase 3b-2): the fact id,
    // the medicine for medicine-level operations, the suspension for an
    // end date. The HLC of a fact is that of the earliest operation with
    // its id, which is the one that recorded it.
    public Guid? EntityId { get; init; }

    public required string Payload { get; init; }

    // Sequence number of the segment that published the operation; null
    // while it is pending (Phase 3c).
    public int? SegmentSeq { get; set; }

    public HybridTimestamp Timestamp => new(HlcPhysicalMs, HlcCounter, DeviceId);
}
