using MedReminder.Application.Abstractions;
using MedReminder.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class SentEmailNotificationRepository : ISentEmailNotificationRepository
{
    private readonly MedReminderDbContext _db;

    public SentEmailNotificationRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public Task<SentEmailNotification?> GetLatestForMedicineAsync(Guid medicineId, CancellationToken cancellationToken)
        => _db.SentEmailNotifications
            .AsNoTracking()
            .Where(e => e.MedicineId == medicineId)
            .OrderByDescending(e => e.SentAt)
            // Same instant: the later stage is the latest email.
            .ThenByDescending(e => e.Stage)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
        => _db.SentEmailNotifications.AnyAsync(e => e.Id == id, cancellationToken);

    public async Task AddAsync(SentEmailNotification sent, CancellationToken cancellationToken)
    {
        await _db.SentEmailNotifications.AddAsync(sent, cancellationToken);
    }
}
