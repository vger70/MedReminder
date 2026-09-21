using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class MedicineRepository : IMedicineRepository
{
    private readonly MedReminderDbContext _db;

    public MedicineRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public Task<Medicine?> GetAsync(Guid id, CancellationToken cancellationToken)
        => _db.Medicines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Medicine>> ListActiveAsync(CancellationToken cancellationToken)
    {
        return await _db.Medicines
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Medicine>> ListActiveWithDoseReminderAsync(
        CancellationToken cancellationToken)
    {
        return await _db.Medicines
            .AsNoTracking()
            .Where(m => m.IsActive && m.RemindOnDose)
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Medicine>> ListAllAsync(CancellationToken cancellationToken)
    {
        return await _db.Medicines
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Medicine medicine, CancellationToken cancellationToken)
    {
        await _db.Medicines.AddAsync(medicine, cancellationToken);
    }

    public Task UpdateAsync(Medicine medicine, CancellationToken cancellationToken)
    {
        // Idempotent: if already tracked, no need to touch the state;
        // if untracked, attach it and force Modified.
        var entry = _db.Entry(medicine);
        if (entry.State == EntityState.Detached)
        {
            _db.Medicines.Update(medicine);
        }
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}
