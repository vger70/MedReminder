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
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(NotificationEvent evt, CancellationToken cancellationToken)
    {
        await _db.NotificationEvents.AddAsync(evt, cancellationToken);
    }
}
