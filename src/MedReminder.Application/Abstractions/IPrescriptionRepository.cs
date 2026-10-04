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

// Dispensations of repeatable prescriptions (replicated). A dispensation
// whose prescription is gone may exist after a sync (see
// DispensationChanged); readers ignore it.
public interface IPrescriptionDispensationRepository
{
    Task<PrescriptionDispensation?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrescriptionDispensation>> ListAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PrescriptionDispensation>> ListForPrescriptionAsync(
        Guid prescriptionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrescriptionDispensation>> ListForMedicineAsync(Guid medicineId, CancellationToken cancellationToken);

    // Dispensations recorded per prescription id (of one medicine when
    // medicineId is set): the count Prescription.StatusOn needs.
    Task<IReadOnlyDictionary<Guid, int>> CountByPrescriptionAsync(Guid? medicineId, CancellationToken cancellationToken);

    Task AddAsync(PrescriptionDispensation dispensation, CancellationToken cancellationToken);

    Task UpdateAsync(PrescriptionDispensation dispensation, CancellationToken cancellationToken);

    Task RemoveAsync(PrescriptionDispensation dispensation, CancellationToken cancellationToken);
}
