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

    // One-off backfill (B.1 Phase 2d): events recorded without an
    // EpochFactId get the id of the fact that opened their epoch, from
    // the epoch number. Events whose epoch is not in the map keep null.
    Task AssignEpochFactIdsAsync(
        Guid medicineId,
        IReadOnlyDictionary<int, Guid> epochFactIds,
        CancellationToken cancellationToken);
}
