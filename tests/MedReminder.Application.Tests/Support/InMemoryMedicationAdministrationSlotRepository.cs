using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryMedicationAdministrationSlotRepository
    : IMedicationAdministrationSlotRepository
{
    private readonly List<MedicationAdministrationSlotSet> _sets = new();
    private readonly List<MedicationAdministrationSlot> _items = new();

    // Every slot row ever recorded, across all sets (history included).
    public IReadOnlyList<MedicationAdministrationSlot> All => _items;

    public IReadOnlyList<MedicationAdministrationSlotSet> Sets => _sets;

    public Task<IReadOnlyList<MedicationAdministrationSlot>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        var latest = _sets
            .Where(s => s.MedicineId == medicineId)
            .OrderByDescending(s => s.RecordedAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefault();
        IReadOnlyList<MedicationAdministrationSlot> result = latest is null
            ? []
            : _items.Where(s => s.SetId == latest.Id).OrderBy(s => s.Order).ToList();
        return Task.FromResult(result);
    }

    public Task AddSetAsync(
        MedicationAdministrationSlotSet set,
        IReadOnlyList<MedicationAdministrationSlot> slots,
        CancellationToken cancellationToken)
    {
        if (slots.Any(s => s.SetId != set.Id || s.MedicineId != set.MedicineId))
        {
            throw new ArgumentException("Every slot must belong to the given set and medicine.", nameof(slots));
        }
        _sets.Add(set);
        _items.AddRange(slots);
        return Task.CompletedTask;
    }
}
