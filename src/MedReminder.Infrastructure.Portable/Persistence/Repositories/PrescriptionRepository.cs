using MedReminder.Application.Abstractions;
using MedReminder.Domain.Prescriptions;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class PrescriptionRepository : IPrescriptionRepository
{
    private readonly MedReminderDbContext _db;

    public PrescriptionRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    // Tracked, so a later update in the same unit of work changes it.
    public async Task<Prescription?> GetAsync(Guid id, CancellationToken cancellationToken)
        => await _db.Prescriptions.FindAsync([id], cancellationToken);

    public async Task<IReadOnlyList<Prescription>> ListAllAsync(CancellationToken cancellationToken)
        => await _db.Prescriptions.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Prescription>> ListForMedicineAsync(Guid medicineId, CancellationToken cancellationToken)
        => await _db.Prescriptions.AsNoTracking().Where(p => p.MedicineId == medicineId).ToListAsync(cancellationToken);

    public async Task AddAsync(Prescription prescription, CancellationToken cancellationToken)
        => await _db.Prescriptions.AddAsync(prescription, cancellationToken);

    public Task UpdateAsync(Prescription prescription, CancellationToken cancellationToken)
    {
        if (_db.Entry(prescription).State == EntityState.Detached) _db.Prescriptions.Update(prescription);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Prescription prescription, CancellationToken cancellationToken)
    {
        _db.Prescriptions.Remove(prescription);
        return Task.CompletedTask;
    }
}

internal sealed class PrescriptionReminderEventRepository : IPrescriptionReminderEventRepository
{
    private readonly MedReminderDbContext _db;

    public PrescriptionReminderEventRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(Guid prescriptionId, DateOnly validUntil, CancellationToken cancellationToken)
        => _db.PrescriptionReminderEvents.AnyAsync(
            e => e.PrescriptionId == prescriptionId && e.ValidUntil == validUntil, cancellationToken);

    public async Task AddAsync(PrescriptionReminderEvent reminder, CancellationToken cancellationToken)
        => await _db.PrescriptionReminderEvents.AddAsync(reminder, cancellationToken);
}

internal sealed class ShortageNoticeEventRepository : MedReminder.Application.Catalogue.IShortageNoticeEventRepository
{
    private readonly MedReminderDbContext _db;

    public ShortageNoticeEventRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(Guid medicineId, string code, DateOnly start, CancellationToken cancellationToken)
        => _db.ShortageNoticeEvents.AnyAsync(
            e => e.MedicineId == medicineId && e.Code == code && e.Start == start, cancellationToken);

    public async Task AddAsync(MedReminder.Domain.Catalogue.ShortageNoticeEvent notice, CancellationToken cancellationToken)
        => await _db.ShortageNoticeEvents.AddAsync(notice, cancellationToken);
}
