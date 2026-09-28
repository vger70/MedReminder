using MedReminder.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

// Tracked removals, so the rows go in the same SaveChanges as the
// operation log entry, and EF orders the deletes along the foreign keys
// (every table referring to Medicines uses Restrict).
internal sealed class MedicineDeletionRepository : IMedicineDeletionRepository
{
    private readonly MedReminderDbContext _db;

    public MedicineDeletionRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task RemoveAsync(Guid medicineId, CancellationToken cancellationToken)
    {
        var medicine = await _db.Medicines.FirstOrDefaultAsync(m => m.Id == medicineId, cancellationToken);
        if (medicine is null) return;

        _db.MedicationAdministrationSlots.RemoveRange(
            await _db.MedicationAdministrationSlots.Where(s => s.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.MedicationAdministrationSlotSets.RemoveRange(
            await _db.MedicationAdministrationSlotSets.Where(s => s.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.MedicationScheduleHistories.RemoveRange(
            await _db.MedicationScheduleHistories.Where(s => s.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.MedicineActivityChanges.RemoveRange(
            await _db.MedicineActivityChanges.Where(a => a.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.StockMovements.RemoveRange(
            await _db.StockMovements.Where(m => m.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.MedicationIntakes.RemoveRange(
            await _db.MedicationIntakes.Where(i => i.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.StockCounts.RemoveRange(
            await _db.StockCounts.Where(c => c.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.MedicationSuspensions.RemoveRange(
            await _db.MedicationSuspensions.Where(s => s.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.FactRetractions.RemoveRange(
            await _db.FactRetractions.Where(r => r.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.NotificationEvents.RemoveRange(
            await _db.NotificationEvents.Where(e => e.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.DoseReminderEvents.RemoveRange(
            await _db.DoseReminderEvents.Where(e => e.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.SyncFieldVersions.RemoveRange(
            await _db.SyncFieldVersions.Where(v => v.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.SyncConflicts.RemoveRange(
            await _db.SyncConflicts.Where(c => c.MedicineId == medicineId).ToListAsync(cancellationToken));
        _db.Medicines.Remove(medicine);
    }
}
