using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

public interface IMedicationScheduleHistoryRepository
{
    Task<IReadOnlyList<MedicationScheduleHistory>> ListForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    Task AddAsync(MedicationScheduleHistory entry, CancellationToken cancellationToken);
}
