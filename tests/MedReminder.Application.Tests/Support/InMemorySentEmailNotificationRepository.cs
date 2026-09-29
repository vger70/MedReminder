using MedReminder.Application.Abstractions;
using MedReminder.Domain.Notifications;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemorySentEmailNotificationRepository : ISentEmailNotificationRepository
{
    private readonly List<SentEmailNotification> _items = new();

    public IReadOnlyList<SentEmailNotification> All => _items;

    public Task<SentEmailNotification?> GetLatestForMedicineAsync(Guid medicineId, CancellationToken cancellationToken)
        => Task.FromResult(_items.Where(e => e.MedicineId == medicineId).MaxBy(e => e.SentAt));

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_items.Any(e => e.Id == id));

    public void RemoveForMedicine(Guid medicineId) => _items.RemoveAll(e => e.MedicineId == medicineId);

    public Task AddAsync(SentEmailNotification sent, CancellationToken cancellationToken)
    {
        _items.Add(sent);
        return Task.CompletedTask;
    }
}
