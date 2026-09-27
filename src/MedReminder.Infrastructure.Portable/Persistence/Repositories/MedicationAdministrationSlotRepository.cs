using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class MedicationAdministrationSlotRepository
    : IMedicationAdministrationSlotRepository
{
    private readonly MedReminderDbContext _db;

    public MedicationAdministrationSlotRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MedicationAdministrationSlot>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        return await _db.MedicationAdministrationSlots
            .AsNoTracking()
            .Where(s => s.MedicineId == medicineId)
            .OrderBy(s => s.Order)
            .ToListAsync(cancellationToken);
    }

    public async Task AddRangeAsync(
        IEnumerable<MedicationAdministrationSlot> slots, CancellationToken cancellationToken)
    {
        await _db.MedicationAdministrationSlots.AddRangeAsync(slots, cancellationToken);
    }

    public async Task DeleteForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        // Do not use ExecuteDeleteAsync (EF Core 7+) so we stay
        // inside the current unit of work: loading the slots and
        // removing them through the change tracker ensures the use
        // case's SaveChangesAsync commits delete + insert atomically.
        var existing = await _db.MedicationAdministrationSlots
            .Where(s => s.MedicineId == medicineId)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _db.MedicationAdministrationSlots.RemoveRange(existing);
        }
    }
}
