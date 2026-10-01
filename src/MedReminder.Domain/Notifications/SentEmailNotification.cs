namespace MedReminder.Domain.Notifications;

// A low-stock email sent for one stock epoch of a medicine, by any device
// of the profile's sync group. A replicated fact (operation
// EmailNotificationSent): a device that is about to email for an epoch
// another device already emailed for skips the email, while its own
// toast still follows the device-local NotificationEvent. Without sync it
// is recorded all the same and gives the same answer as NotificationEvent.
public sealed class SentEmailNotification
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required int StockEpoch { get; init; }

    // Medicine.StockEpochFactId when the email was sent; null only for a
    // medicine whose epoch has no fact id (NotificationCycle then
    // compares epoch numbers).
    public Guid? EpochFactId { get; init; }

    public required DateTimeOffset SentAt { get; init; }
}
