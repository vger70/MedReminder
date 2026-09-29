namespace MedReminder.Domain.Sync;

// One version of a last-writer-wins register (table SyncFieldVersions,
// B.1 Phase 3b, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2, §7.3).
// Every version is kept, not only the winner: concurrency and conflicts
// are decided from the whole set, and Phase 3b-2 evaluates stock counts
// on the register values "as of" their timestamp.
//
// Registers (SyncRegisters, Application): each replicated medicine field
// and IsActive (entity = medicine), EndDate of a suspension (entity =
// suspension), the medicine's slot set and its latest schedule row, and
// one register per schedule date (value = the id of the row or set
// written). A register with no version holds its genesis value, which
// is older than any version.
public sealed class SyncFieldVersion
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required Guid EntityId { get; init; }

    public required string Register { get; init; }

    public required long HlcPhysicalMs { get; init; }

    public required int HlcCounter { get; init; }

    public required Guid DeviceId { get; init; }

    public string? Value { get; init; }

    public long? BasePhysicalMs { get; init; }

    public int? BaseCounter { get; init; }

    public Guid? BaseDeviceId { get; init; }

    public HybridTimestamp Version => new(HlcPhysicalMs, HlcCounter, DeviceId);

    public HybridTimestamp? Base => BasePhysicalMs is { } ms
        ? new HybridTimestamp(ms, BaseCounter ?? 0, BaseDeviceId ?? Guid.Empty)
        : null;
}
