namespace MedReminder.Domain.Stock;

// A stock count the user asserted: "on CountDay I counted
// CountedQuantity, of which TakenToday already taken that day"
// (B.1, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §3.4, §4.3 rule 3).
// It is the fact behind a ReconcileStock correction; the correction
// itself stays a derived stock movement. ThresholdAtCount keeps the
// medicine's threshold at the moment of the count, which the epoch
// rule needs.
//
// Phase 2b only creates the table; ReconcileStock starts recording
// counts in Phase 2c together with LedgerDeriver.
public sealed class StockCount
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required DateOnly CountDay { get; init; }

    public required decimal CountedQuantity { get; init; }

    public required decimal TakenToday { get; init; }

    public required int ThresholdAtCount { get; init; }

    // Recording instant. Phase 3 adds the hybrid logical clock.
    public required DateTimeOffset RecordedAt { get; init; }
}
