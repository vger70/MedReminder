using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

public interface IMedicineRepository
{
    Task<Medicine?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Medicine>> ListActiveAsync(CancellationToken cancellationToken);

    // Active medicines with RemindOnDose == true. Used by
    // DoseReminderService on every minute tick (ANALYSIS-A5 §4.2).
    Task<IReadOnlyList<Medicine>> ListActiveWithDoseReminderAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Medicine>> ListAllAsync(CancellationToken cancellationToken);
    Task AddAsync(Medicine medicine, CancellationToken cancellationToken);
    Task UpdateAsync(Medicine medicine, CancellationToken cancellationToken);
}
