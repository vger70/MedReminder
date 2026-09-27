using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

// Activation / deactivation history of a medicine (B.1 Phase 2c-2, D15).
public interface IMedicineActivityRepository
{
    // In recording order.
    Task<IReadOnlyList<MedicineActivityChange>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken);

    Task AddAsync(MedicineActivityChange change, CancellationToken cancellationToken);
}
