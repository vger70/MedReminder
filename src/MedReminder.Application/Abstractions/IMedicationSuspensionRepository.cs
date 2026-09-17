using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

public interface IMedicationSuspensionRepository
{
    Task<IReadOnlyList<MedicationSuspension>> ListForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    // Currently open suspension (EndDate = null) for the medicine;
    // null if the medicine is not suspended.
    Task<MedicationSuspension?> GetOpenSuspensionAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    Task AddAsync(MedicationSuspension suspension, CancellationToken cancellationToken);
    Task UpdateAsync(MedicationSuspension suspension, CancellationToken cancellationToken);
}
