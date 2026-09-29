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
        // Latest set by recording instant; Id breaks an (unlikely) tie
        // deterministically. SetId is compared column to column, so the
        // stored Guid text format does not matter.
        var latestSet = _db.MedicationAdministrationSlotSets
            .Where(s => s.MedicineId == medicineId)
            .OrderByDescending(s => s.RecordedAt)
            .ThenByDescending(s => s.Id)
            .Select(s => s.Id)
            .Take(1);

        return await _db.MedicationAdministrationSlots
            .AsNoTracking()
            .Where(s => latestSet.Contains(s.SetId))
            .OrderBy(s => s.Order)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdministrationSlotSetEntry>> ListSetsForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        var sets = await _db.MedicationAdministrationSlotSets
            .AsNoTracking()
            .Where(s => s.MedicineId == medicineId)
            .OrderBy(s => s.RecordedAt)
            .ToListAsync(cancellationToken);
        var slots = await _db.MedicationAdministrationSlots
            .AsNoTracking()
            .Where(s => s.MedicineId == medicineId)
            .ToListAsync(cancellationToken);
        return sets
            .Select(set => new AdministrationSlotSetEntry(
                set,
                slots.Where(s => s.SetId == set.Id).OrderBy(s => s.Order).ToList()))
            .ToList();
    }

    public async Task AddSetAsync(
        MedicationAdministrationSlotSet set,
        IReadOnlyList<MedicationAdministrationSlot> slots,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Any(s => s.SetId != set.Id || s.MedicineId != set.MedicineId))
        {
            throw new ArgumentException("Every slot must belong to the given set and medicine.", nameof(slots));
        }

        await _db.MedicationAdministrationSlotSets.AddAsync(set, cancellationToken);
        await _db.MedicationAdministrationSlots.AddRangeAsync(slots, cancellationToken);
    }
}
