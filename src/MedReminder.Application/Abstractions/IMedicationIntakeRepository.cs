using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

public interface IMedicationIntakeRepository
{
    Task<IReadOnlyList<MedicationIntake>> ListForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    // Elenco dei giorni (fuso locale) per i quali esiste una registrazione
    // manuale nell'intervallo indicato. Usato dal ConsumptionCatchUp per
    // saltare quelle giornate — l'assunzione manuale prevale sul
    // materializzatore automatico.
    Task<IReadOnlyList<DateOnly>> ListManualIntakeDaysAsync(
        Guid medicineId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken);

    Task AddAsync(MedicationIntake intake, CancellationToken cancellationToken);
}
