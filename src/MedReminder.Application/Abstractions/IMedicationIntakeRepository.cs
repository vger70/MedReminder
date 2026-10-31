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

    // The intakes of one medicine on `day`, for the dose slots they
    // cover (DoseSlots.Records).
    Task<IReadOnlyList<MedicationIntake>> ListForDayAsync(
        Guid medicineId, DateOnly day, CancellationToken cancellationToken)
        => ListForMedicineBetweenAsync(medicineId, day, day, cancellationToken);

    // The intakes of one medicine from `fromInclusive` to `toInclusive`
    // (days in the local zone), ordered by day: a log over a period reads
    // only that period, not the whole history (mobile issue #153).
    async Task<IReadOnlyList<MedicationIntake>> ListForMedicineBetweenAsync(
        Guid medicineId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken cancellationToken)
        => [.. (await ListForMedicineAsync(medicineId, cancellationToken))
            .Where(i => i.Day >= fromInclusive && i.Day <= toInclusive)
            .OrderBy(i => i.Day)];

    // One intake of a medicine by id, null when there is none: a
    // primary-key read where the sync apply and the retraction looked
    // through the whole history (issue #328).
    async Task<MedicationIntake?> GetAsync(Guid medicineId, Guid intakeId, CancellationToken cancellationToken)
        => (await ListForMedicineAsync(medicineId, cancellationToken)).FirstOrDefault(i => i.Id == intakeId);

    Task AddAsync(MedicationIntake intake, CancellationToken cancellationToken);

    // Retraction of a mistaken intake (RetractFact, B.1 Phase 2d).
    Task RemoveAsync(MedicationIntake intake, CancellationToken cancellationToken);
}
