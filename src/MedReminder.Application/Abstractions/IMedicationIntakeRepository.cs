using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

public interface IMedicationIntakeRepository
{
    Task<IReadOnlyList<MedicationIntake>> ListForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    // Days (local zone) for which a manual intake record exists in the
    // given range. Used by ConsumptionCatchUp to skip those days — a
    // manual intake wins over the automatic materializer.
    Task<IReadOnlyList<DateOnly>> ListManualIntakeDaysAsync(
        Guid medicineId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken);

    Task AddAsync(MedicationIntake intake, CancellationToken cancellationToken);
}
