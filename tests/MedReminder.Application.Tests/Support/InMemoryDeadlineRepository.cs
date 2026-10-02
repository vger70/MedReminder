using MedReminder.Application.Abstractions;
using MedReminder.Domain.Deadlines;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryDeadlineRepository : IDeadlineRepository
{
    private readonly List<Deadline> _items = new();

    public IReadOnlyList<Deadline> All => _items;

    public Task<Deadline?> GetAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_items.FirstOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<Deadline>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Deadline>>(_items.ToList());

    public Task AddAsync(Deadline deadline, CancellationToken cancellationToken)
    {
        _items.Add(deadline);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Deadline deadline, CancellationToken cancellationToken)
    {
        var index = _items.FindIndex(d => d.Id == deadline.Id);
        if (index >= 0) _items[index] = deadline;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Deadline deadline, CancellationToken cancellationToken)
    {
        _items.RemoveAll(d => d.Id == deadline.Id);
        return Task.CompletedTask;
    }

    public void RemoveForMedicine(Guid medicineId) => _items.RemoveAll(d => d.MedicineId == medicineId);
}

internal sealed class InMemoryDeadlineReminderEventRepository : IDeadlineReminderEventRepository
{
    private readonly List<DeadlineReminderEvent> _items = new();

    public IReadOnlyList<DeadlineReminderEvent> All => _items;

    public Task<bool> ExistsAsync(Guid deadlineId, DateOnly dueOn, CancellationToken cancellationToken)
        => Task.FromResult(_items.Any(e => e.DeadlineId == deadlineId && e.DueOn == dueOn));

    public Task AddAsync(DeadlineReminderEvent reminder, CancellationToken cancellationToken)
    {
        _items.Add(reminder);
        return Task.CompletedTask;
    }

    public void RemoveForMedicine(Guid medicineId) => _items.RemoveAll(e => e.MedicineId == medicineId);
}
