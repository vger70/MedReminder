using MedReminder.Application.Abstractions;
using MedReminder.Application.Deadlines;
using MedReminder.Application.Packages;
using MedReminder.Application.Prescriptions;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Notifications;

// A planned notification with the device text the pass would show for it.
public sealed record PlannedNotificationText(PlannedNotification Notice, string Title, string Body);

// The device texts of planned notifications, built by the same functions
// the passes use (DoseReminderService, MedicationMonitor,
// PackageExpiryNotices, PrescriptionReminders, DeadlineReminders), with the
// values of the day each notice is planned for. For hosts that hand the
// notifications to the platform ahead of time because they cannot run a
// pass at the instant (iOS). Pure.
public static class PlannedNotificationTexts
{
    private static readonly IReadOnlyDictionary<Guid, int> NoDispensations = new Dictionary<Guid, int>();

    // Null when the plan's state no longer holds what the notice is about.
    // medicineNames: the names of the medicines deadlines belong to, which
    // may be missing from `medicines` (no device channel).
    public static (string Title, string Body)? Build(
        PlannedNotification notice,
        IReadOnlyDictionary<Guid, MedicinePlanState> medicines,
        DeadlinePlanState? deadlines,
        IReadOnlyDictionary<Guid, string> medicineNames,
        ILocalizationService? localization)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(medicines);
        ArgumentNullException.ThrowIfNull(medicineNames);

        if (notice.Kind == PlannedNotificationKind.Deadline)
        {
            var deadline = deadlines?.Deadlines.FirstOrDefault(d => d.Id == notice.DeadlineId);
            if (deadline is null) return null;
            var name = deadline.MedicineId is { } id ? medicineNames.GetValueOrDefault(id) : null;
            return DeadlineReminders.BuildTexts(deadline, name, notice.Day, localization);
        }

        if (!medicines.TryGetValue(notice.MedicineId, out var state)) return null;
        var medicine = state.Medicine;
        switch (notice.Kind)
        {
            case PlannedNotificationKind.DoseReminder when notice.SlotTime is { } slotTime:
                return NotificationTexts.BuildDoseReminder(medicine, slotTime, localization);
            case PlannedNotificationKind.LowStock when notice.DaysRemaining is { } days:
                return NotificationTexts.BuildToast(medicine, days, localization: localization, stage: notice.Stage,
                    repeatable: notice.RepeatablePrescription);
            case PlannedNotificationKind.PackageExpiry when notice.ExpiresOn is { } earliest:
                return PackageExpiryNotices.BuildTexts(medicine,
                    notice.Stage == PackageExpiryNoticeEvent.ExpiredStage, notice.PackageIds?.Count ?? 1, earliest,
                    localization);
            case PlannedNotificationKind.PrescriptionValidity when notice.ValidUntil is { } until:
                var prescription = state.Prescriptions?.FirstOrDefault(p => p.Id == notice.PrescriptionId);
                if (prescription is null) return null;
                var collected = prescription.IsRepeatable
                    ? (state.DispensationsCollected ?? NoDispensations).GetValueOrDefault(prescription.Id)
                    : 0;
                return PrescriptionReminders.BuildTexts(medicine, prescription, until, collected, localization);
            default:
                return null;
        }
    }
}
