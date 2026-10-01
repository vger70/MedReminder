using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;

namespace MedReminder.Domain.Calculations;

// Pure rule that decides whether a medicine is inside the warning
// window and has not already been notified successfully for the
// current epoch. Calling this function does NOT send anything: it just
// answers "should I notify?" (spec §8).
//
// Two warning stages per epoch (docs/notes/EVOLUTION-PROPOSALS-2.md
// §3.1): the first when the days remaining reach the medicine's
// threshold, the second when they reach half of it and the stock has
// still not been replenished (no new epoch). A medicine that enters the
// window already below half gets the second stage only: it covers the
// first. Without the second stage a user who ignored the first warning
// heard nothing more until the medicine ran out.
public static class NotificationCycle
{
    public const int FirstStage = 1;
    public const int SecondStage = 2;

    // Days remaining at or below which the second warning is due: half
    // of the threshold, rounded down. A threshold of 1 gives 0, i.e. the
    // day the stock is estimated to run out.
    public static int SecondWarningDays(int thresholdDays) => Math.Max(thresholdDays, 0) / 2;

    // latestNotificationForMedicine: the last NotificationEvent
    // recorded for this medicine (irrespective of epoch and outcome).
    // The function decides autonomously whether that event neutralizes
    // the current cycle (a successful event on the current epoch at the
    // same or a later stage = "already notified").
    public static bool ShouldNotify(
        Medicine medicine,
        int? daysRemaining,
        DateOnly? estimatedRunOutDate,
        NotificationEvent? latestNotificationForMedicine)
        => StageToNotify(medicine, daysRemaining, estimatedRunOutDate, latestNotificationForMedicine) is not null;

    // The stage to notify now, or null when nothing is due.
    public static int? StageToNotify(
        Medicine medicine,
        int? daysRemaining,
        DateOnly? estimatedRunOutDate,
        NotificationEvent? latestNotificationForMedicine)
    {
        ArgumentNullException.ThrowIfNull(medicine);

        if (!medicine.IsActive) return null;
        if (medicine.NotificationChannels == NotificationChannels.None) return null;
        if (daysRemaining is null) return null;
        if (daysRemaining > medicine.ThresholdDays) return null;

        // If the therapy ends before the estimated run-out, no
        // warning: a new prescription is not needed (spec Q2 in
        // ANALYSIS §1.3).
        if (estimatedRunOutDate is not null
            && medicine.EndDate is not null
            && estimatedRunOutDate > medicine.EndDate)
        {
            return null;
        }

        var stage = StageFor(medicine.ThresholdDays, daysRemaining.Value);

        // A successful event for the current epoch at this stage or a
        // later one suppresses the resend. A failed event does NOT
        // block: the Application / Infrastructure handles the retry
        // with back-off; here the domain just says "still to be
        // notified". A successful event on a previous epoch (a refill
        // happened in the meantime) does not block: the cycle restarts
        // with the epoch.
        if (latestNotificationForMedicine is { } evt
            && IsSameEpoch(evt, medicine)
            && evt.Success
            && evt.Stage >= stage)
        {
            return null;
        }

        return stage;
    }

    // Stage of a medicine inside its warning window.
    public static int StageFor(int thresholdDays, int daysRemaining)
    {
        var second = SecondWarningDays(thresholdDays);
        return second < thresholdDays && daysRemaining <= second ? SecondStage : FirstStage;
    }

    // Whether the low-stock email of the medicine's current epoch, at
    // this stage or a later one, was already sent, by this device or by
    // another device of the sync group (SentEmailNotification is
    // replicated). The caller then leaves the email channel out and still
    // shows its own toast.
    public static bool EmailAlreadySent(Medicine medicine, SentEmailNotification? latestEmailForMedicine,
        int stage = FirstStage)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        return latestEmailForMedicine is { } sent
            && IsSameEpoch(sent.EpochFactId, sent.StockEpoch, medicine)
            && sent.Stage >= stage;
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
