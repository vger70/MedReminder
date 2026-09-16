using MedReminder.Application.Abstractions;
using MedReminder.Domain.Notifications;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryNotificationEventRepository
    : INotificationEventRepository
{
    private readonly List<NotificationEvent> _items = new();

    public IReadOnlyList<NotificationEvent> All => _items;

    public Task<NotificationEvent?> GetLatestForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        var latest = _items
            .Where(e => e.MedicineId == medicineId)
            .OrderByDescending(e => e.TriggeredAt)
            .FirstOrDefault();
        return Task.FromResult<NotificationEvent?>(latest);
    }

    public Task AddAsync(NotificationEvent evt, CancellationToken cancellationToken)
    {
        _items.Add(evt);
        return Task.CompletedTask;
    }
}
