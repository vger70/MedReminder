using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Notifications;

// Formats the texts (subject / body for email, title / body for
// toast) from the medicine state. No clinical information (spec §22):
// only medicine identification + invitation to request a prescription,
// or, when the medicine has a repeatable prescription with dispensations
// left (RepeatablePrescriptionNotice), to collect the next dispensation.
//
// Localization: every text (email, low-stock toast, dose reminder)
// uses the language chosen by the user in the app
// (loc.CurrentLanguage) through loc.Get(...). Toasts used to follow
// the Windows UI language, which showed them in a different language
// than the rest of the app whenever the two differed.
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
        ILocalizationService? localization = null,
        int stage = NotificationCycle.FirstStage,
        RepeatablePrescriptionNotice? repeatable = null)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        var c = culture ?? localization?.CurrentCulture ?? CultureInfo.CurrentCulture;
        var second = stage >= NotificationCycle.SecondStage;

        string subject;
        var body = new System.Text.StringBuilder();

        if (localization is not null)
        {
            subject = localization.Get(second ? "Notifications.Email.SubjectSecond" : "Notifications.Email.Subject",
                medicine.Name, daysRemaining);

            body.Append(localization.Get(second ? "Notifications.Email.HeaderSecond" : "Notifications.Email.Header"))
                .Append('\n').Append('\n');
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
            if (repeatable is not null)
            {
                body.Append(localization.Get("Notifications.Email.CallToActionRepeatable", repeatable.DispensationsLeft))
                    .Append('\n');
                if (repeatable.ValidUntil is { } until)
                {
                    body.Append(localization.Get("Notifications.Repeatable.ValidUntil", until.ToString("d", c))).Append('\n');
                }
            }
            else
            {
                body.Append(localization.Get("Notifications.Email.CallToAction")).Append('\n');
            }
            body.Append('\n');
            body.Append(localization.Get("Notifications.Email.Footer"));
        }
        else
        {
            // Backwards compatibility: English hardcoded texts for the
            // tests that do not pass ILocalizationService.
            subject = second
                ? $"MedReminder — second reminder: {medicine.Name} running low ({daysRemaining} days)"
                : $"MedReminder — {medicine.Name} running low ({daysRemaining} days)";
            body.Append(second
                    ? "MedReminder second reminder: the stock has not been replenished since the first reminder."
                    : "MedReminder reminder.")
                .Append('\n').Append('\n');
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
            if (repeatable is not null)
            {
                body.Append($"Dispensations left on the repeatable prescription: {repeatable.DispensationsLeft}. "
                    + "Collect the next one at the pharmacy.").Append('\n');
                if (repeatable.ValidUntil is { } until)
                {
                    body.Append($"Valid until {until.ToString("d", c)}.").Append('\n');
                }
            }
            else
            {
                body.Append("It is advisable to request a new prescription from your doctor in advance.").Append('\n');
            }
            body.Append('\n');
            body.Append("— MedReminder (organizational reminder, not a medical device).");
        }

        return new EmailMessage(subject, body.ToString(), Kind: EmailKind.LowStock);
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
        if (slot.IsAsNeeded)
        {
            sb.Append(" (as needed)");
        }
        return sb.ToString();
    }

    // Windows low-stock toast, in the user's app language.
    public static (string Title, string Body) BuildToast(
        Medicine medicine,
        int daysRemaining,
        CultureInfo? culture = null,
        ILocalizationService? localization = null,
        int stage = NotificationCycle.FirstStage,
        RepeatablePrescriptionNotice? repeatable = null)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        var c = culture ?? localization?.CurrentCulture ?? CultureInfo.CurrentCulture;
        var second = stage >= NotificationCycle.SecondStage;

        if (localization is not null)
        {
            var title = localization.Get(second ? "Notifications.Toast.TitleSecond" : "Notifications.Toast.Title",
                medicine.Name, daysRemaining);
            if (repeatable is not null)
            {
                var text = localization.Get(
                    second ? "Notifications.Toast.BodySecondRepeatable" : "Notifications.Toast.BodyRepeatable",
                    daysRemaining, repeatable.DispensationsLeft);
                if (repeatable.ValidUntil is { } until)
                {
                    text += " " + localization.Get("Notifications.Repeatable.ValidUntil", until.ToString("d", c));
                }
                return (title, text);
            }
            var body = localization.Get(second ? "Notifications.Toast.BodySecond" : "Notifications.Toast.Body",
                daysRemaining);
            return (title, body);
        }

        if (repeatable is not null)
        {
            var text = (second ? "Not replenished yet. " : string.Empty)
                + $"Estimated quantity for {daysRemaining} days. "
                + $"Dispensations left on the repeatable prescription: {repeatable.DispensationsLeft}.";
            if (repeatable.ValidUntil is { } until) text += $" Valid until {until.ToString("d", c)}.";
            return (second ? $"Second reminder — {medicine.Name}: {daysRemaining} days left"
                : $"{medicine.Name}: {daysRemaining} days left", text);
        }

        // Backwards compatibility: hardcoded EN.
        if (second)
        {
            return ($"Second reminder — {medicine.Name}: {daysRemaining} days left",
                $"Not replenished yet. Estimated quantity for {daysRemaining} days. "
                + "Request a new prescription soon.");
        }
        var titleEn = $"{medicine.Name}: {daysRemaining} days left";
        var bodyEn = $"Estimated quantity for {daysRemaining} days. "
                   + "Consider requesting a new prescription.";
        return (titleEn, bodyEn);
    }

    // Dose-time reminder: fired once per (medicine, slot, local-day)
    // at the slot's wall-clock time. Used for both the toast and the
    // email, in the user's app language like BuildToast.
    public static (string Title, string Body) BuildDoseReminder(
        Medicine medicine,
        TimeOnly slotTime,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(medicine);

        if (localization is not null)
        {
            var title = localization.Get(
                "Notifications.DoseReminder.Title", medicine.Name);
            var body = localization.Get(
                "Notifications.DoseReminder.Body",
                medicine.Name,
                slotTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            return (title, body);
        }

        // Backwards compatibility: hardcoded EN.
        return (
            $"Time to take {medicine.Name}",
            $"{medicine.Name} — scheduled dose at {slotTime:HH:mm}");
    }

    // Returns the ISO 639-1 language code matching the user's Windows
    // language (CultureInfo.CurrentUICulture); falls back to "en" if
    // the language is not among the app's supported ones. Used only to
    // pick the initial app language on first run. Must stay aligned
    // with SupportedLanguages.All.
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

// A repeatable prescription of the medicine to collect, with
// dispensations left: the low-stock warning points to it instead of a
// new prescription. ValidUntil null when the prescription has no end.
public sealed record RepeatablePrescriptionNotice(int DispensationsLeft, DateOnly? ValidUntil);
