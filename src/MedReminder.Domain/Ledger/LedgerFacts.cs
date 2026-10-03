using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Domain.Ledger;

// Replicated facts of one medicine, the only input of LedgerDeriver
// (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §3.4, §4.3). The caller
// resolves everything that depends on persistence or on the freeze:
//
//   CutoffDay     -> the profile's LedgerCutoff.CutoffDay when the
//                    medicine existed at the freeze, otherwise the day
//                    before StartDate. Days up to it are frozen.
//   BaselineEpoch -> the medicine's StockEpoch at the freeze (1 when it
//                    did not exist yet). Legacy stock entries are
//                    already counted in it.
//   Legacy*       -> rows and intakes recorded before the freeze.
//
// Lists that are "in recording order" stand in for the recording
// clock until Phase 3 adds the hybrid logical clock.
public sealed record LedgerFacts
{
    public required Guid MedicineId { get; init; }

    public required DateOnly StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public required DateOnly CutoffDay { get; init; }

    public int BaselineEpoch { get; init; } = 1;

    // Every movement recorded before the freeze (Origin Legacy).
    public IReadOnlyList<StockMovement> LegacyMovements { get; init; } = [];

    // Stock facts the user entered after the freeze (Origin User):
    // initial load, new package, manual add, positive / negative
    // correction. Derived movements are never an input.
    public IReadOnlyList<StockMovement> UserEntries { get; init; } = [];

    // Every intake, legacy or not, in recording order.
    public IReadOnlyList<LedgerIntake> Intakes { get; init; } = [];

    // Schedule rows in recording order. For two rows with the same
    // EffectiveFrom the later one wins (§17).
    public IReadOnlyList<MedicationScheduleHistory> Schedule { get; init; } = [];

    public IReadOnlyList<MedicationSuspension> Suspensions { get; init; } = [];

    public IReadOnlyList<LedgerSlotSet> SlotSets { get; init; } = [];

    // Activity changes (D15). No change in force on a day means active.
    public IReadOnlyList<LedgerActivity> Activity { get; init; } = [];

    // Stock counts in recording order, each with the outcome evaluated
    // when it was recorded (LedgerDeriver.EvaluateCount).
    public IReadOnlyList<StockCountAnchor> Counts { get; init; } = [];
}

public sealed record LedgerIntake(
    Guid Id,
    DateOnly Day,
    IntakeStatus Status,
    decimal Quantity,
    DateTimeOffset RecordedAt,
    bool IsLegacy,
    string? Notes = null,
    bool IsExtra = false);

// One recorded slot set (MedicationAdministrationSlotSet and its rows).
public sealed record LedgerSlotSet(
    DateOnly EffectiveFrom,
    DateTimeOffset RecordedAt,
    IReadOnlyList<MedicationAdministrationSlot> Slots,
    Guid Id = default);

// "From Day on the medicine is active / inactive", recorded at
// RecordedAt. Among the changes with Day <= d, the latest recorded
// wins.
public sealed record LedgerActivity(
    DateOnly Day,
    bool Active,
    DateTimeOffset RecordedAt,
    Guid Id = default);

// A stock count and its outcome, evaluated on the facts recorded
// before it (§4.3 rule 3). The outcome is stored with the fact on a
// single device (Phase 2c); Phase 3 re-evaluates it when facts from
// other devices arrive.
public sealed record StockCountAnchor(
    Guid Id,
    DateOnly CountDay,
    DateTimeOffset RecordedAt,
    decimal CountedQuantity,
    decimal TakenToday,
    int ThresholdAtCount,
    decimal LedgerAtStartOfDay,
    decimal CountDayScheduled,
    decimal Correction,
    bool MaterializesCountDay,
    bool AdvancesEpoch,
    string? Notes = null);
