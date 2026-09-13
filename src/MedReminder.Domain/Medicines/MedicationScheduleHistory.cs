namespace MedReminder.Domain.Medicines;

// Versione dello schema di assunzione per una data medicina. Ogni cambio
// di dose o di somministrazioni giornaliere crea un nuovo record con
// EffectiveFrom = data del cambio. Il consumo giornaliero di un giorno D
// viene risolto scegliendo il record con EffectiveFrom <= D più recente
// (DailyConsumption.RateOn).
public sealed class MedicationScheduleHistory
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required DateOnly EffectiveFrom { get; init; }

    public required decimal DosePerAdministration { get; init; }

    public required int AdministrationsPerDay { get; init; }
}
