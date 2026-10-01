using MedReminder.Domain.Notifications;

namespace MedReminder.Application.Abstractions;

// Low-stock emails sent for a medicine, by this device or by another
// device of the sync group (replicated facts).
public interface ISentEmailNotificationRepository
{
    // The most recent one for the medicine, whatever its epoch:
    // NotificationCycle.EmailAlreadySent decides whether it covers the
    // current epoch.
    Task<SentEmailNotification?> GetLatestForMedicineAsync(Guid medicineId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(SentEmailNotification sent, CancellationToken cancellationToken);
}
