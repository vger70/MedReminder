using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Notifications;

// Formats the texts (subject / body for email, title / body for
// toast) from the medicine state. No clinical information (spec §22):
// only medicine identification + invitation to request a prescription.
//
// Localization (Increment 16d):
//   - BuildEmail uses the USER language (loc.CurrentLanguage), so it
//     calls loc.Get(...) directly.
//   - BuildToast uses the SYSTEM language
//     (CultureInfo.CurrentUICulture), so it calls
//     loc.GetIn(systemLanguageCode, ...).
// If `loc` is null (older tests), the hardcoded English texts are used
// — backwards compatibility.
public static class NotificationTexts
{
    public static EmailMessage BuildEmail(
        Medicine medicine,
        decimal currentStock,
        int daysRemaining,
        DateOnly? estimatedRunOutDate,
        CultureInfo? culture = null,
        IReadOnlyList<MedicationAdministrationSlot>? administrationSlots = null,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        var c = culture ?? localization?.CurrentCulture ?? CultureInfo.CurrentCulture;

        string subject;
        var body = new System.Text.StringBuilder();

        if (localization is not null)
        {
            subject = localization.Get("Notifications.Email.Subject", medicine.Name, daysRemaining);

            body.Append(localization.Get("Notifications.Email.Header")).Append('\n').Append('\n');
            body.Append(localization.Get("Notifications.Email.Medicine", medicine.Name)).Append('\n');
            if (!string.IsNullOrWhiteSpace(medicine.ActiveIngredient))
            {
                body.Append(localization.Get("Notifications.Email.ActiveIngredient", medicine.ActiveIngredient)).Append('\n');
            }
            body.Append(localization.Get("Notifications.Email.Stock",
                currentStock.ToString("0.##", c), medicine.Unit)).Append('\n');
            body.Append(localization.Get("Notifications.Email.DaysLeft", daysRemaining)).Append('\n');
            if (estimatedRunOutDate is not null)
            {
                body.Append(localization.Get("Notifications.Email.RunOutDate",
                    estimatedRunOutDate.Value.ToString("d", c))).Append('\n');
            }
            if (administrationSlots is { Count: > 0 } slots)
            {
                body.Append(localization.Get("Notifications.Email.Dosage.Section")).Append('\n');
                foreach (var slot in slots.OrderBy(s => s.Time.HasValue ? 0 : 1).ThenBy(s => s.Time).ThenBy(s => s.Order))
                {
                    body.Append("  - ").Append(FormatSlotForEmail(slot, medicine.Unit, c)).Append('\n');
                }
            }
            if (!string.IsNullOrWhiteSpace(medicine.DoctorName))
            {
                body.Append(localization.Get("Notifications.Email.Doctor", medicine.DoctorName)).Append('\n');
            }
            body.Append('\n');
            body.Append(localization.Get("Notifications.Email.CallToAction")).Append('\n');
            body.Append('\n');
            body.Append(localization.Get("Notifications.Email.Footer"));
        }
        else
        {
            // Backwards compatibility: English hardcoded texts for the
            // tests that do not pass ILocalizationService.
            subject = $"MedReminder — {medicine.Name} running low ({daysRemaining} days)";
            body.Append("MedReminder reminder.").Append('\n').Append('\n');
            body.Append($"Medicine: {medicine.Name}").Append('\n');
            if (!string.IsNullOrWhiteSpace(medicine.ActiveIngredient))
            {
                body.Append($"Active ingredient: {medicine.ActiveIngredient}").Append('\n');
            }
            body.Append($"Remaining quantity: {currentStock.ToString("0.##", c)} {medicine.Unit}").Append('\n');
            body.Append($"Estimated days left: {daysRemaining}").Append('\n');
            if (estimatedRunOutDate is not null)
            {
                body.Append($"Estimated run-out date: {estimatedRunOutDate.Value.ToString("d", c)}").Append('\n');
            }
            if (administrationSlots is { Count: > 0 } slots)
            {
                body.Append("Dosage:").Append('\n');
                foreach (var slot in slots.OrderBy(s => s.Time.HasValue ? 0 : 1).ThenBy(s => s.Time).ThenBy(s => s.Order))
                {
                    body.Append("  - ").Append(FormatSlotForEmail(slot, medicine.Unit, c)).Append('\n');
                }
            }
            if (!string.IsNullOrWhiteSpace(medicine.DoctorName))
            {
                body.Append($"Reference doctor: {medicine.DoctorName}").Append('\n');
            }
            body.Append('\n');
            body.Append("It is advisable to request a new prescription from your doctor in advance.").Append('\n');
            body.Append('\n');
            body.Append("— MedReminder (organizational reminder, not a medical device).");
        }

        return new EmailMessage(subject, body.ToString());
    }

    private static string FormatSlotForEmail(MedicationAdministrationSlot slot, string unit, CultureInfo c)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(slot.Dose.ToString("0.##", c)).Append(' ').Append(unit);
        if (!string.IsNullOrWhiteSpace(slot.TimingLabel))
        {
            sb.Append(' ').Append(slot.TimingLabel);
        }
        if (slot.Time is { } t)
        {
            sb.Append(" (").Append(t.ToString("HH:mm", c)).Append(')');
        }
        return sb.ToString();
    }

    // Windows toast: uses the SYSTEM language (systemLanguageCode).
    // The caller (MedicationMonitor) detects the system language with
    // DetectSystemLanguageCode() and passes it here; the service uses
    // GetIn(languageCode, key) to bypass the user language.
    public static (string Title, string Body) BuildToast(
        Medicine medicine,
        int daysRemaining,
        CultureInfo? culture = null,
        ILocalizationService? localization = null,
        string? systemLanguageCode = null)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        _ = culture;

        if (localization is not null)
        {
            var langCode = systemLanguageCode ?? DetectSystemLanguageCode();
            var title = localization.GetIn(langCode, "Notifications.Toast.Title",
                medicine.Name, daysRemaining);
            var body = localization.GetIn(langCode, "Notifications.Toast.Body", daysRemaining);
            return (title, body);
        }

        // Backwards compatibility: hardcoded EN.
        var titleEn = $"{medicine.Name}: {daysRemaining} days left";
        var bodyEn = $"Estimated quantity for {daysRemaining} days. "
                   + "Consider requesting a new prescription.";
        return (titleEn, bodyEn);
    }

    // Returns the ISO 639-1 language code matching the user's Windows
    // language (CultureInfo.CurrentUICulture); falls back to "en" if
    // the language is not among the app's supported ones. Must stay
    // aligned with SupportedLanguages.All.
    public static string DetectSystemLanguageCode()
    {
        var twoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return twoLetter switch
        {
            "it" => "it",
            "fr" => "fr",
            "es" => "es",
            "de" => "de",
            _ => "en",
        };
    }
}
