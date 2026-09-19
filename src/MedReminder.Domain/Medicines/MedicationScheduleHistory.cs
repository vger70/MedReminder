namespace MedReminder.Domain.Medicines;

// Version of the intake schedule for a given medicine. Every change of
// dose, of daily administrations or of schedule shape creates a new
// record with EffectiveFrom = the date of the change. Daily
// consumption for a day D is resolved by picking the record with the
// most recent EffectiveFrom <= D (DailyConsumption.RateOn).
//
// Since A1 (docs/ANALYSIS-A1-REGIMENS.md §2.2) each entry carries a
// discriminated Schedule shape:
//  - ScheduleKind = FixedDaily (default) — the legacy fields
//    DosePerAdministration and AdministrationsPerDay describe the
//    rate; SchedulePayload is null.
//  - Any other Kind — SchedulePayload carries the JSON payload
//    reconstructed by ScheduleCodec; the legacy fields are kept as
//    display-only fallbacks and are ignored by the projection.
public sealed class MedicationScheduleHistory
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required DateOnly EffectiveFrom { get; init; }

    public required decimal DosePerAdministration { get; init; }

    public required int AdministrationsPerDay { get; init; }

    // Default FixedDaily so pre-A1 rows read back with legacy
    // semantics without a data-fix pass. Persisted as an INTEGER
    // with DEFAULT 0 (see DatabaseInitializer).
    public ScheduleKind ScheduleKind { get; init; } = ScheduleKind.FixedDaily;

    // JSON payload matching ScheduleKind (see ScheduleCodec). Null
    // for FixedDaily.
    public string? SchedulePayload { get; init; }
}
