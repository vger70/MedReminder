using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Deadlines;

// Reminder of an administrative deadline (docs/notes/
// EVOLUTION-PROPOSALS-2.md §3.6): from LeadDays before the date on,
// once per deadline and date on this device, on the deadline's own
// channels. Email only where this device sends email (the master device
// of a shared installation), like the low-stock warning.
//
// Called by MedicationMonitor inside its WriteGate pass. Neither the
// label nor the medicine name is logged.
public sealed class DeadlineReminders
{
    private readonly IDeadlineRepository _deadlines;
    private readonly IDeadlineReminderEventRepository _events;
    private readonly IMedicineRepository _medicines;
    private readonly IEmailNotificationService _email;
    private readonly IWindowsNotificationService _windows;
    private readonly TimeProvider _clock;
    private readonly ILogger<DeadlineReminders> _log;
    private readonly ILocalizationService? _localization;

    public DeadlineReminders(
        IDeadlineRepository deadlines,
        IDeadlineReminderEventRepository events,
        IMedicineRepository medicines,
        IEmailNotificationService email,
        IWindowsNotificationService windows,
        TimeProvider clock,
        ILogger<DeadlineReminders> log,
        ILocalizationService? localization = null)
    {
        _deadlines = deadlines;
        _events = events;
        _medicines = medicines;
        _email = email;
        _windows = windows;
        _clock = clock;
        _log = log;
        _localization = localization;
    }

    // Returns the number of reminders shown or sent. The caller saves.
    public async Task<int> RunAsync(DateOnly today, bool sendsEmail, CancellationToken cancellationToken)
    {
        var sent = 0;
        foreach (var deadline in await _deadlines.ListAllAsync(cancellationToken))
        {
            if (!DeadlineRules.ReminderDue(deadline, today)) continue;
            if (await _events.ExistsAsync(deadline.Id, deadline.DueOn, cancellationToken)) continue;

            var channels = deadline.Channels;
            if (!sendsEmail) channels &= ~NotificationChannels.Email;
            if (channels == NotificationChannels.None) continue;

            string? medicineName = null;
            if (deadline.MedicineId is { } medicineId)
            {
                medicineName = (await _medicines.GetAsync(medicineId, cancellationToken))?.Name;
            }

            var any = false;
            var (title, body) = BuildTexts(deadline, medicineName, today);
            if ((channels & NotificationChannels.Windows) != 0)
            {
                try
                {
                    await _windows.ShowAsync(title, body, NotificationTarget.Deadline(deadline.MedicineId),
                        cancellationToken);
                    any = true;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Deadline reminder toast failed for deadline {DeadlineId}", deadline.Id);
                }
            }
            if ((channels & NotificationChannels.Email) != 0)
            {
                try
                {
                    await _email.SendAsync(new EmailMessage(title, BuildEmailBody(body), Kind: EmailKind.Deadline),
                        cancellationToken);
                    any = true;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Deadline reminder email failed for deadline {DeadlineId}", deadline.Id);
                }
            }
            // A failed attempt is retried on the next pass.
            if (!any) continue;
            await _events.AddAsync(new DeadlineReminderEvent
            {
                DeadlineId = deadline.Id,
                MedicineId = deadline.MedicineId,
                DueOn = deadline.DueOn,
                FiredAt = _clock.GetUtcNow(),
            }, cancellationToken);
            sent++;
        }
        return sent;
    }

    private (string Title, string Body) BuildTexts(Deadline deadline, string? medicineName, DateOnly today)
    {
        var c = _localization?.CurrentCulture ?? CultureInfo.CurrentCulture;
        var subject = DeadlineTexts.Subject(deadline, medicineName, _localization);
        var date = deadline.DueOn.ToString("d", c);
        var overdue = today > deadline.DueOn;
        if (_localization is null)
        {
            return ($"Deadline: {subject}",
                overdue
                    ? $"{subject} was due on {date}. Mark it done in MedReminder once it is renewed."
                    : $"{subject} is due on {date}.");
        }
        return (_localization.Get("Notifications.Deadline.Title", subject),
            _localization.Get(overdue ? "Notifications.Deadline.Body.Overdue" : "Notifications.Deadline.Body", subject, date));
    }

    private string BuildEmailBody(string body)
        => body + "\n\n" + (_localization?.Get("Notifications.Email.Footer")
            ?? "— MedReminder (organizational reminder, not a medical device).");
}
