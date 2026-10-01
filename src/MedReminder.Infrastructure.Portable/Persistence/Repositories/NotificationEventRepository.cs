using MedReminder.Application.Abstractions;
using MedReminder.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class NotificationEventRepository
    : INotificationEventRepository
{
    private readonly MedReminderDbContext _db;

    public NotificationEventRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public Task<NotificationEvent?> GetLatestForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        return _db.NotificationEvents
            .AsNoTracking()
            .Where(e => e.MedicineId == medicineId)
            .OrderByDescending(e => e.TriggeredAt)
            // Same instant: the later stage is the latest event.
            .ThenByDescending(e => e.Stage)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(NotificationEvent evt, CancellationToken cancellationToken)
    {
        await _db.NotificationEvents.AddAsync(evt, cancellationToken);
    }

    public async Task AssignEpochFactIdsAsync(
        Guid medicineId,
        IReadOnlyDictionary<int, Guid> epochFactIds,
        CancellationToken cancellationToken)
    {
        // Tracked query: the values are written by the caller's
        // SaveChangesAsync, in the same unit of work as the derivation.
        var events = await _db.NotificationEvents
            .Where(e => e.MedicineId == medicineId && e.EpochFactId == null)
            .ToListAsync(cancellationToken);
        foreach (var evt in events)
        {
            if (epochFactIds.TryGetValue(evt.StockEpoch, out var factId))
            {
                _db.Entry(evt).Property(e => e.EpochFactId).CurrentValue = factId;
            }
        }
    }
}
