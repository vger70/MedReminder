namespace MedReminder.Domain.Medicines;

// Administration slot: describes ONE single daily intake of a medicine
// — with a dose, an optional time and a free-form label that the user
// sees on the therapy card ("in the morning, on an empty stomach",
// "after dinner", etc.).
//
// Simple, per-medicine model (no schedule-history versioning): when the
// user changes their times, they replace the current list of slots. The
// history of past times is not preserved — it is not needed by the
// stock monitor and would complicate the model for no benefit.
//
// Composition rule with MedicationScheduleHistory:
//  - if a medicine HAS slots, daily consumption = SUM(slot.Dose).
//  - if it has no slots, the legacy formula is used:
//    Medicine.DosePerAdministration × Medicine.AdministrationsPerDay
//    (Increment 1..9 behavior).
// See DailyConsumption.RateOn.
public sealed class MedicationAdministrationSlot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    // Dose of the single intake, in the medicine's unit.
    public required decimal Dose { get; init; }

    // Exact time (local zone) if specified by the user; null if the
    // user preferred to only provide a descriptive label.
    public TimeOnly? Time { get; init; }

    // Free-form description of the moment of the day ("in the morning",
    // "before sleep", "after dinner on a full stomach", etc.). Picked
    // from a preset or typed.
    public string? TimingLabel { get; init; }

    // Display order: the UI shows slots ordered by
    // (Time NULLS LAST, Order), so entries "without time" stay at the
    // end or in the order chosen by the user.
    public int Order { get; init; }
}
