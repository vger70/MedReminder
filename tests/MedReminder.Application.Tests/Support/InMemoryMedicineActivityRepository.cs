using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryMedicineActivityRepository : IMedicineActivityRepository
{
    private readonly List<MedicineActivityChange> _items = new();

    public IReadOnlyList<MedicineActivityChange> All => _items;

    public Task<IReadOnlyList<MedicineActivityChange>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MedicineActivityChange> result =
            _items.Where(a => a.MedicineId == medicineId).OrderBy(a => a.RecordedAt).ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(MedicineActivityChange change, CancellationToken cancellationToken)
    {
        _items.Add(change);
        return Task.CompletedTask;
    }

    // Test double of IMedicineDeletionRepository (InMemoryMedicineDeletionRepository).
    public void RemoveForMedicine(Guid medicineId)
    {
        _items.RemoveAll(x => x.MedicineId == medicineId);
    }
}
