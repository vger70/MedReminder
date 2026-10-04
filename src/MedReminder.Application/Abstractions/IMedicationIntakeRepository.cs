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

    // Medicines with an intake on `day` that handles the day, i.e. not an
    // extra one (ANALYSIS-INTRADAY-CONSUMPTION.md §5.3). One query for the
    // whole list.
    Task<IReadOnlyList<Guid>> ListMedicinesWithDayIntakeAsync(DateOnly day, CancellationToken cancellationToken);

    Task AddAsync(MedicationIntake intake, CancellationToken cancellationToken);

    // Retraction of a mistaken intake (RetractFact, B.1 Phase 2d).
    Task RemoveAsync(MedicationIntake intake, CancellationToken cancellationToken);
}
