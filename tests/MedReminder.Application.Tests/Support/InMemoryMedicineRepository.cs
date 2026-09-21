using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryMedicineRepository : IMedicineRepository
{
    private readonly Dictionary<Guid, Medicine> _items = new();

    public Task<Medicine?> GetAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_items.TryGetValue(id, out var m) ? m : null);

    public Task<IReadOnlyList<Medicine>> ListActiveAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Medicine> result = _items.Values.Where(m => m.IsActive).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Medicine>> ListActiveWithDoseReminderAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Medicine> result = _items.Values.Where(m => m.IsActive && m.RemindOnDose).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Medicine>> ListAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Medicine> result = _items.Values.ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(Medicine medicine, CancellationToken cancellationToken)
    {
        _items[medicine.Id] = medicine;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Medicine medicine, CancellationToken cancellationToken)
    {
        _items[medicine.Id] = medicine;
        return Task.CompletedTask;
    }
}
