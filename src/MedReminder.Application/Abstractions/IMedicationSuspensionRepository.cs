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

    // One suspension of a medicine by id, null when there is none.
    async Task<MedicationSuspension?> GetAsync(Guid medicineId, Guid suspensionId, CancellationToken cancellationToken)
        => (await ListForMedicineAsync(medicineId, cancellationToken)).FirstOrDefault(s => s.Id == suspensionId);

    Task AddAsync(MedicationSuspension suspension, CancellationToken cancellationToken);
    Task UpdateAsync(MedicationSuspension suspension, CancellationToken cancellationToken);

    // Retraction of a mistaken suspension (RetractFact, B.1 Phase 2d).
    Task RemoveAsync(MedicationSuspension suspension, CancellationToken cancellationToken);
}
