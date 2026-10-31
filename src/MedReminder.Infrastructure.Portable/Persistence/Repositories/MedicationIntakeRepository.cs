using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class MedicationIntakeRepository : IMedicationIntakeRepository
{
    private readonly MedReminderDbContext _db;

    public MedicationIntakeRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MedicationIntake>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        return await _db.MedicationIntakes
            .AsNoTracking()
            .Where(i => i.MedicineId == medicineId)
            .OrderBy(i => i.Day)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DateOnly>> ListManualIntakeDaysAsync(
        Guid medicineId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken)
    {
        return await Between(medicineId, fromInclusive, toInclusive)
            .Select(i => i.Day)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListMedicinesWithDayIntakeAsync(
        DateOnly day, CancellationToken cancellationToken)
        => await _db.MedicationIntakes
            .AsNoTracking()
            .Where(i => i.Day == day && !i.IsExtra)
            .Select(i => i.MedicineId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MedicationIntake>> ListForDayAsync(
        Guid medicineId, DateOnly day, CancellationToken cancellationToken)
        => await Between(medicineId, day, day).ToListAsync(cancellationToken);

    // One range read on the (MedicineId, Day) index.
    public async Task<IReadOnlyList<MedicationIntake>> ListForMedicineBetweenAsync(
        Guid medicineId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken cancellationToken)
        => await Between(medicineId, fromInclusive, toInclusive)
            .OrderBy(i => i.Day)
            .ToListAsync(cancellationToken);

    // On the primary key.
    public Task<MedicationIntake?> GetAsync(Guid medicineId, Guid intakeId, CancellationToken cancellationToken)
        => _db.MedicationIntakes
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == intakeId && i.MedicineId == medicineId, cancellationToken);

    // The intakes of one medicine from `fromInclusive` to `toInclusive`,
    // both included: a range on the (MedicineId, Day) index.
    private IQueryable<MedicationIntake> Between(Guid medicineId, DateOnly fromInclusive, DateOnly toInclusive)
        => _db.MedicationIntakes
            .AsNoTracking()
            .Where(i => i.MedicineId == medicineId && i.Day >= fromInclusive && i.Day <= toInclusive);

    public async Task AddAsync(MedicationIntake intake, CancellationToken cancellationToken)
    {
        await _db.MedicationIntakes.AddAsync(intake, cancellationToken);
    }

    public Task RemoveAsync(MedicationIntake intake, CancellationToken cancellationToken)
    {
        _db.MedicationIntakes.Remove(intake);
        return Task.CompletedTask;
    }
}
