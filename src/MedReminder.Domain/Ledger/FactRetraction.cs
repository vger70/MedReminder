namespace MedReminder.Domain.Ledger;

// Tombstone of a retracted fact (B.1 Phase 2d, D8,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2, §7.3). The fact row is
// removed; the tombstone stays, keyed by the fact id, so that in Phase 3
// a copy of the fact arriving from another device is ignored.
public sealed class FactRetraction
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required Guid FactId { get; init; }

    public required FactKind Kind { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }
}
