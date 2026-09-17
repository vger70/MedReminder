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
            && evt.StockEpoch == medicine.StockEpoch
            && evt.Success)
        {
            return false;
        }

        return true;
    }
}
