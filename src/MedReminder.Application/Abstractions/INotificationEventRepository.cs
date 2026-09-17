using MedReminder.Domain.Notifications;

namespace MedReminder.Application.Abstractions;

public interface INotificationEventRepository
{
    // Most recent notification recorded for the medicine (regardless
    // of outcome and epoch): the suppression logic is decided by
    // NotificationCycle.
    Task<NotificationEvent?> GetLatestForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    Task AddAsync(NotificationEvent evt, CancellationToken cancellationToken);
}
