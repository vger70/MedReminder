namespace MedReminder.Domain.Medicines;

// Administration slot: describes ONE single daily intake of a medicine
// — with a dose, an optional time and a free-form label that the user
// sees on the therapy card ("in the morning, on an empty stomach",
// "after dinner", etc.).
//
// Every slot belongs to a MedicationAdministrationSlotSet (B.1 Phase
// 2b). When the user changes their times, a new set is recorded and
// becomes the current list; earlier sets are kept as history for the
// ledger derivation (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2).
// Only the current set is used by today's calculations.
//
// Composition rule with MedicationScheduleHistory:
//  - if a medicine HAS slots and its schedule is FixedDaily, daily
//    consumption = SUM(slot.Dose) over the slots that are not
//    as-needed (IsAsNeeded); when every slot is as-needed, daily
//    consumption is 0, as for a PRN schedule.
//  - with any other schedule (weekly, cyclic, tapering) the schedule
//    gives the day's quantity and the slots split it in proportion to
//    their doses: the slots say when, the schedule how much.
//  - if it has no slots, the legacy formula is used:
//    Medicine.DosePerAdministration × Medicine.AdministrationsPerDay
//    (Increment 1..9 behavior).
// See DailyConsumption.RateOn.
public sealed class MedicationAdministrationSlot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    // Set this slot belongs to (MedicationAdministrationSlotSet.Id).
    // Guid.Empty only in code that builds slots outside persistence,
    // such as calculation tests.
    public Guid SetId { get; init; }

    // Dose of the single intake, in the medicine's unit.
    public required decimal Dose { get; init; }

    // Exact time (local zone) if specified by the user; null if the
    // user preferred to only provide a descriptive label.
    public TimeOnly? Time { get; init; }

    // Free-form description of the moment of the day ("in the morning",
    // "before sleep", "after dinner on a full stomach", etc.). Picked
    // from a preset or typed.
    public string? TimingLabel { get; init; }

    // As-needed dose (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md
    // §5.1): taken only when needed and recorded by the user, never
    // consumed automatically. Stored on the slot, not read from a
    // preset, because slot sets are history: a later edit elsewhere
    // must not change past days. Rows written before the flag read
    // false.
    public bool IsAsNeeded { get; init; }

    // Time-of-day preset the description was picked from (DoseTimePreset
    // id), used to place a slot without Time in the day. Display only.
    // Null for a typed description and for rows written before presets.
    public Guid? PresetId { get; set; }

    // Display order: the UI shows slots ordered by
    // (Time NULLS LAST, Order), so entries "without time" stay at the
    // end or in the order chosen by the user.
    public int Order { get; init; }
}
