using MedReminder.Domain.Notifications;

namespace MedReminder.Domain.Stock;

// A package expiry notice shown by this device (docs/analysis/
// ANALYSIS-PACKAGE-EXPIRY.md §3.6; not replicated). One per package,
// effective expiry, stage and channel: a changed date (package opened,
// expiry corrected) gets new notices, and a failed email is retried
// while the toast that worked is not shown again.
public sealed class PackageExpiryNoticeEvent
{
    public const int SoonStage = 1;
    public const int ExpiredStage = 2;

    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid PackageId { get; init; }

    // Lets the deletion of a medicine remove its notices.
    public required Guid MedicineId { get; init; }

    public required DateOnly EffectiveExpiry { get; init; }

    public required int Stage { get; init; }

    // Windows or Email, one channel per row.
    public required NotificationChannels Channel { get; init; }

    public required DateTimeOffset FiredAt { get; init; }
}
