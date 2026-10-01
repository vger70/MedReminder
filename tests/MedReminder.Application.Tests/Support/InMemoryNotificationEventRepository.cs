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
            .ThenByDescending(e => e.Stage)
            .FirstOrDefault();
        return Task.FromResult<NotificationEvent?>(latest);
    }

    public Task AddAsync(NotificationEvent evt, CancellationToken cancellationToken)
    {
        _items.Add(evt);
        return Task.CompletedTask;
    }

    // NotificationEvent is init-only: replace the instance.
    public Task AssignEpochFactIdsAsync(
        Guid medicineId, IReadOnlyDictionary<int, Guid> epochFactIds, CancellationToken cancellationToken)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            var e = _items[i];
            if (e.MedicineId != medicineId || e.EpochFactId is not null) continue;
            if (!epochFactIds.TryGetValue(e.StockEpoch, out var factId)) continue;
            _items[i] = new NotificationEvent
            {
                Id = e.Id,
                MedicineId = e.MedicineId,
                StockEpoch = e.StockEpoch,
                TriggeredAt = e.TriggeredAt,
                Channel = e.Channel,
                DaysRemainingAtSend = e.DaysRemainingAtSend,
                Success = e.Success,
                ErrorMessage = e.ErrorMessage,
                EpochFactId = factId,
            };
        }
        return Task.CompletedTask;
    }

    // Test double of IMedicineDeletionRepository (InMemoryMedicineDeletionRepository).
    public void RemoveForMedicine(Guid medicineId)
    {
        _items.RemoveAll(x => x.MedicineId == medicineId);
    }
}
