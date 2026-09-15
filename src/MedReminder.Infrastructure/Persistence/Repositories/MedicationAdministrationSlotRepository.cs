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
        // Non uso ExecuteDeleteAsync (EF Core 7+) per restare all'interno
        // dell'unità di lavoro corrente: caricare gli slot e rimuoverli
        // via change tracker garantisce che il SaveChangesAsync del use
        // case commetta delete + insert atomically.
        var existing = await _db.MedicationAdministrationSlots
            .Where(s => s.MedicineId == medicineId)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _db.MedicationAdministrationSlots.RemoveRange(existing);
        }
    }
}
