namespace MedReminder.Domain.Medicines;

// Version of the intake schedule for a given medicine. Every change of
// dose or of daily administrations creates a new record with
// EffectiveFrom = the date of the change. Daily consumption for a day
// D is resolved by picking the record with the most recent
// EffectiveFrom <= D (DailyConsumption.RateOn).
public sealed class MedicationScheduleHistory
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required DateOnly EffectiveFrom { get; init; }

    public required decimal DosePerAdministration { get; init; }

    public required int AdministrationsPerDay { get; init; }
}
