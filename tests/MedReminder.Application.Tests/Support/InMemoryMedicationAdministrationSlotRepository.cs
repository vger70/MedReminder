using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryMedicationAdministrationSlotRepository
    : IMedicationAdministrationSlotRepository
{
    private readonly List<MedicationAdministrationSlot> _items = new();

    public IReadOnlyList<MedicationAdministrationSlot> All => _items;

    public Task<IReadOnlyList<MedicationAdministrationSlot>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MedicationAdministrationSlot> result =
            _items.Where(s => s.MedicineId == medicineId)
                  .OrderBy(s => s.Order)
                  .ToList();
        return Task.FromResult(result);
    }

    public Task AddRangeAsync(
        IEnumerable<MedicationAdministrationSlot> slots, CancellationToken cancellationToken)
    {
        _items.AddRange(slots);
        return Task.CompletedTask;
    }

    public Task DeleteForMedicineAsync(Guid medicineId, CancellationToken cancellationToken)
    {
        _items.RemoveAll(s => s.MedicineId == medicineId);
        return Task.CompletedTask;
    }
}
