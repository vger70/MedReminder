using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class MedicationSuspensionRepository
    : IMedicationSuspensionRepository
{
    private readonly MedReminderDbContext _db;

    public MedicationSuspensionRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MedicationSuspension>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        return await _db.MedicationSuspensions
            .AsNoTracking()
            .Where(s => s.MedicineId == medicineId)
            .OrderBy(s => s.StartDate)
            .ToListAsync(cancellationToken);
    }

    public Task<MedicationSuspension?> GetOpenSuspensionAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        return _db.MedicationSuspensions
            .FirstOrDefaultAsync(
                s => s.MedicineId == medicineId && s.EndDate == null,
                cancellationToken);
    }

    public async Task AddAsync(MedicationSuspension suspension, CancellationToken cancellationToken)
    {
        await _db.MedicationSuspensions.AddAsync(suspension, cancellationToken);
    }

    public Task UpdateAsync(MedicationSuspension suspension, CancellationToken cancellationToken)
    {
        var entry = _db.Entry(suspension);
        if (entry.State == EntityState.Detached)
        {
            _db.MedicationSuspensions.Update(suspension);
        }
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}
