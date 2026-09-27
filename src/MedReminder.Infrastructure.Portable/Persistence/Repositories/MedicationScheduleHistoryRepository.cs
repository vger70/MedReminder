using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class MedicationScheduleHistoryRepository
    : IMedicationScheduleHistoryRepository
{
    private readonly MedReminderDbContext _db;

    public MedicationScheduleHistoryRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MedicationScheduleHistory>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        return await _db.MedicationScheduleHistories
            .AsNoTracking()
            .Where(s => s.MedicineId == medicineId)
            .OrderBy(s => s.EffectiveFrom)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(MedicationScheduleHistory entry, CancellationToken cancellationToken)
    {
        await _db.MedicationScheduleHistories.AddAsync(entry, cancellationToken);
    }
}
