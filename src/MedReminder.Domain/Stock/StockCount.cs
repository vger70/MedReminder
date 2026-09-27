namespace MedReminder.Domain.Stock;

// A stock count the user asserted: "on CountDay I counted
// CountedQuantity, of which TakenToday already taken that day"
// (B.1, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §3.4, §4.3 rule 3).
// It is the fact behind a ReconcileStock correction; the correction
// itself stays a derived stock movement. ThresholdAtCount keeps the
// medicine's threshold at the moment of the count, which the epoch
// rule needs.
//
// Written by ReconcileStock since Phase 2c-2, with the outcome
// evaluated at that moment on the facts recorded before the count
// (LedgerDeriver.EvaluateCount). The ledger derivation books the
// stored outcome; Phase 3 re-evaluates it when facts from other devices
// arrive.
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

    public string? Notes { get; init; }

    // Outcome at recording time (StockCountAnchor).
    public decimal LedgerAtStartOfDay { get; init; }

    public decimal CountDayScheduled { get; init; }

    public decimal Correction { get; init; }

    public bool MaterializesCountDay { get; init; }

    public bool AdvancesEpoch { get; init; }
}
