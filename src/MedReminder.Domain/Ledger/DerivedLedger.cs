using MedReminder.Domain.Stock;

namespace MedReminder.Domain.Ledger;

// Rule that produced a row, part of its deterministic id.
public enum DerivedRule
{
    Legacy = 0,
    UserEntry = 1,
    IntakeConsumption = 2,
    FrozenDayReversal = 3,
    AutomaticConsumption = 4,
    CountDayConsumption = 5,
    CountCorrection = 6,
}

// One row of the derived ledger. Legacy and UserEntry rows are the
// input facts passed through (their own ids); the others are derived
// and carry a name-based id, so a re-derivation replaces them
// idempotently. Epoch is the stock epoch in force at OccurredAt
// (diagnostic, as StockMovement.StockEpoch); 0 on pass-through rows.
public sealed record LedgerRow(
    Guid Id,
    DerivedRule Rule,
    StockMovementKind Kind,
    decimal Delta,
    DateOnly Day,
    DateTimeOffset OccurredAt,
    string? Notes = null,
    int Epoch = 0)
{
    public bool IsDerived => Rule is not (DerivedRule.Legacy or DerivedRule.UserEntry);
}

// RawTotal may be negative (consumption keeps running at zero stock);
// Stock is clamped as MedicineStock.Current does.
// EpochFactIds: the fact that opened each epoch from the baseline to the
// current one (§4.4). The baseline epoch has a name-based id.
public sealed record DerivedLedger(
    IReadOnlyList<LedgerRow> Rows,
    decimal RawTotal,
    decimal Stock,
    int Epoch,
    IReadOnlyDictionary<int, Guid> EpochFactIds)
{
    public Guid EpochFactId => EpochFactIds[Epoch];

    public IEnumerable<LedgerRow> DerivedRows => Rows.Where(r => r.IsDerived);
}
