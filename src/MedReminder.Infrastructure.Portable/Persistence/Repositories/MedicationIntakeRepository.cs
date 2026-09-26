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
        return await _db.MedicationIntakes
            .AsNoTracking()
            .Where(i => i.MedicineId == medicineId
                        && i.Day >= fromInclusive
                        && i.Day <= toInclusive)
            .Select(i => i.Day)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(MedicationIntake intake, CancellationToken cancellationToken)
    {
        await _db.MedicationIntakes.AddAsync(intake, cancellationToken);
    }
}
