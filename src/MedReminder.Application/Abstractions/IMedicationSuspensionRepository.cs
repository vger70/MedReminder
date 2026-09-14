using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

public interface IMedicationSuspensionRepository
{
    Task<IReadOnlyList<MedicationSuspension>> ListForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    // Sospensione attualmente aperta (EndDate = null) per la medicina;
    // null se la medicina non è sospesa.
    Task<MedicationSuspension?> GetOpenSuspensionAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    Task AddAsync(MedicationSuspension suspension, CancellationToken cancellationToken);
    Task UpdateAsync(MedicationSuspension suspension, CancellationToken cancellationToken);
}
