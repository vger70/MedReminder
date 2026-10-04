using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Prescriptions;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Prescriptions;

// Reminder to collect an issued prescription before it lapses
// (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2): from
// PrescriptionRules.ReminderLeadDays before "valid until" to the day
// itself, once per prescription and date on this device, on the
// medicine's channels. Email only where this device sends email (the
// master device of a shared installation), like the low-stock warning.
// A repeatable prescription is reminded only while dispensations are
// left, and the text says how many would be lost.
//
// Called by MedicationMonitor inside its WriteGate pass. Neither the
// medicine name nor the prescription code is logged.
public sealed class PrescriptionReminders
{
    private readonly IPrescriptionRepository _prescriptions;
    private readonly IPrescriptionDispensationRepository _dispensations;
    private readonly IPrescriptionReminderEventRepository _events;
    private readonly IMedicineRepository _medicines;
    private readonly IEmailNotificationService _email;
    private readonly IWindowsNotificationService _windows;
    private readonly TimeProvider _clock;
    private readonly ILogger<PrescriptionReminders> _log;
    private readonly ILocalizationService? _localization;

    public PrescriptionReminders(
        IPrescriptionRepository prescriptions,
        IPrescriptionDispensationRepository dispensations,
        IPrescriptionReminderEventRepository events,
        IMedicineRepository medicines,
        IEmailNotificationService email,
        IWindowsNotificationService windows,
        TimeProvider clock,
        ILogger<PrescriptionReminders> log,
        ILocalizationService? localization = null)
    {
        _prescriptions = prescriptions;
        _dispensations = dispensations;
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
        var collected = await _dispensations.CountByPrescriptionAsync(null, cancellationToken);
        foreach (var prescription in await _prescriptions.ListAllAsync(cancellationToken))
        {
            var count = prescription.IsRepeatable ? collected.GetValueOrDefault(prescription.Id) : 0;
            if (!PrescriptionRules.ReminderDue(prescription, today, count)) continue;
            var until = prescription.ValidUntil!.Value;
            if (await _events.ExistsAsync(prescription.Id, until, cancellationToken)) continue;
            var medicine = await _medicines.GetAsync(prescription.MedicineId, cancellationToken);
            if (medicine is null || !medicine.IsActive) continue;

            var channels = medicine.NotificationChannels;
            if (!sendsEmail) channels &= ~NotificationChannels.Email;
            if (channels == NotificationChannels.None) continue;

            var any = false;
            var (title, body) = BuildTexts(medicine, prescription, until, count);
            if ((channels & NotificationChannels.Windows) != 0)
            {
                try
                {
                    await _windows.ShowAsync(title, body, NotificationTarget.Prescription(medicine.Id),
                        cancellationToken);
                    any = true;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Prescription reminder toast failed for medicine {MedicineId}", medicine.Id);
                }
            }
            if ((channels & NotificationChannels.Email) != 0)
            {
                try
                {
                    await _email.SendAsync(new EmailMessage(title, BuildEmailBody(body), Kind: EmailKind.Prescription),
                        cancellationToken);
                    any = true;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Prescription reminder email failed for medicine {MedicineId}", medicine.Id);
                }
            }
            // A failed attempt is retried on the next pass.
            if (!any) continue;
            await _events.AddAsync(new PrescriptionReminderEvent
            {
                PrescriptionId = prescription.Id,
                MedicineId = prescription.MedicineId,
                ValidUntil = until,
                FiredAt = _clock.GetUtcNow(),
            }, cancellationToken);
            sent++;
        }
        return sent;
    }

    private (string Title, string Body) BuildTexts(
        Medicine medicine, Prescription prescription, DateOnly until, int dispensationsCollected)
    {
        var c = _localization?.CurrentCulture ?? CultureInfo.CurrentCulture;
        var date = until.ToString("d", c);
        if (prescription.IsRepeatable)
        {
            var left = PrescriptionRules.DispensationsLeft(prescription, dispensationsCollected);
            if (_localization is null)
            {
                return ($"Collect the prescription for {medicine.Name}",
                    $"The repeatable prescription for {medicine.Name} is valid until {date}. "
                    + $"Dispensations not collected by then are lost: {left}.");
            }
            return (_localization.Get("Notifications.Prescription.Title", medicine.Name),
                _localization.Get("Notifications.Prescription.BodyRepeatable", medicine.Name, date, left));
        }
        if (_localization is null)
        {
            return ($"Collect the prescription for {medicine.Name}",
                $"The prescription for {medicine.Name} is valid until {date}. Collect it at the pharmacy.");
        }
        return (_localization.Get("Notifications.Prescription.Title", medicine.Name),
            _localization.Get("Notifications.Prescription.Body", medicine.Name, date));
    }

    private string BuildEmailBody(string body)
        => body + "\n\n" + (_localization?.Get("Notifications.Email.Footer")
            ?? "— MedReminder (organizational reminder, not a medical device).");
}
