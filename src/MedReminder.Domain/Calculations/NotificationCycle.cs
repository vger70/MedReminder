using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;

namespace MedReminder.Domain.Calculations;

// Pure rule that decides whether a medicine is inside the warning
// window and has not already been notified successfully for the
// current epoch. Calling this function does NOT send anything: it just
// answers "should I notify?" (spec §8).
public static class NotificationCycle
{
    // latestNotificationForMedicine: the last NotificationEvent
    // recorded for this medicine (irrespective of epoch and outcome).
    // The function decides autonomously whether that event neutralizes
    // the current cycle (a successful event on the current epoch =
    // "already notified").
    public static bool ShouldNotify(
        Medicine medicine,
        int? daysRemaining,
        DateOnly? estimatedRunOutDate,
        NotificationEvent? latestNotificationForMedicine)
    {
        ArgumentNullException.ThrowIfNull(medicine);

        if (!medicine.IsActive) return false;
        if (medicine.NotificationChannels == NotificationChannels.None) return false;
        if (daysRemaining is null) return false;
        if (daysRemaining > medicine.ThresholdDays) return false;

        // If the therapy ends before the estimated run-out, no
        // warning: a new prescription is not needed (spec Q2 in
        // ANALYSIS §1.3).
        if (estimatedRunOutDate is not null
            && medicine.EndDate is not null
            && estimatedRunOutDate > medicine.EndDate)
        {
            return false;
        }

        // A successful event for the current epoch suppresses the
        // resend. A failed event does NOT block: the
        // Application / Infrastructure handles the retry with
        // back-off; here the domain just says "still to be notified".
        // A successful event on a previous epoch (a refill happened in
        // the meantime) does not block: the cycle restarts with the
        // epoch.
        if (latestNotificationForMedicine is { } evt
            && IsSameEpoch(evt, medicine)
            && evt.Success)
        {
            return false;
        }

        return true;
    }

    // Whether the low-stock email of the medicine's current epoch was
    // already sent, by this device or by another device of the sync
    // group (SentEmailNotification is replicated). The caller then leaves
    // the email channel out and still shows its own toast.
    public static bool EmailAlreadySent(Medicine medicine, SentEmailNotification? latestEmailForMedicine)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        return latestEmailForMedicine is { } sent
            && IsSameEpoch(sent.EpochFactId, sent.StockEpoch, medicine);
    }

    private static bool IsSameEpoch(NotificationEvent evt, Medicine medicine)
        => IsSameEpoch(evt.EpochFactId, evt.StockEpoch, medicine);

    // The epoch is identified by the fact that opened it when both sides
    // know it (B.1 Phase 2d): epoch numbers can be reused after a
    // retraction. Older events fall back to the number.
    private static bool IsSameEpoch(Guid? factId, int epoch, Medicine medicine)
        => factId is { } recordedFact && medicine.StockEpochFactId is { } currentFact
            ? recordedFact == currentFact
            : epoch == medicine.StockEpoch;
}
