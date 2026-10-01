using MedReminder.Domain.Prescriptions;

namespace MedReminder.Application.Abstractions;

// Prescriptions of the profile (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2).
public interface IPrescriptionRepository
{
    Task<Prescription?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Prescription>> ListAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Prescription>> ListForMedicineAsync(Guid medicineId, CancellationToken cancellationToken);

    Task AddAsync(Prescription prescription, CancellationToken cancellationToken);

    Task UpdateAsync(Prescription prescription, CancellationToken cancellationToken);

    Task RemoveAsync(Prescription prescription, CancellationToken cancellationToken);
}

// Reminders to collect a prescription shown by this device (not
// replicated), keyed on (PrescriptionId, ValidUntil).
public interface IPrescriptionReminderEventRepository
{
    Task<bool> ExistsAsync(Guid prescriptionId, DateOnly validUntil, CancellationToken cancellationToken);

    Task AddAsync(PrescriptionReminderEvent reminder, CancellationToken cancellationToken);
}
