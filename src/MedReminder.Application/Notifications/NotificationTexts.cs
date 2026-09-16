using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Notifications;

// Formatta i testi (subject/body per email, title/body per toast) a
// partire dallo stato della medicina. Nessuna indicazione clinica
// (spec §22): solo identificazione della medicina + invito a richiedere
// prescrizione.
//
// Localizzazione (Incremento 16d):
//   - BuildEmail usa la lingua UTENTE (loc.CurrentLanguage), pertanto
//     usa loc.Get(...) direttamente.
//   - BuildToast usa la lingua SISTEMA (CultureInfo.CurrentUICulture),
//     pertanto usa loc.GetIn(systemLanguageCode, ...).
// Se `loc` è null (test esistenti), si ricade sui testi italiani
// hardcoded — retrocompatibilità.
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
            // Retrocompatibilità: testi italiani hardcoded per i test
            // che non passano ILocalizationService.
            subject = $"MedReminder — {medicine.Name} in esaurimento ({daysRemaining} gg)";
            body.Append("Promemoria MedReminder.").Append('\n').Append('\n');
            body.Append($"Medicina: {medicine.Name}").Append('\n');
            if (!string.IsNullOrWhiteSpace(medicine.ActiveIngredient))
            {
                body.Append($"Principio attivo: {medicine.ActiveIngredient}").Append('\n');
            }
            body.Append($"Quantità residua: {currentStock.ToString("0.##", c)} {medicine.Unit}").Append('\n');
            body.Append($"Giorni residui stimati: {daysRemaining}").Append('\n');
            if (estimatedRunOutDate is not null)
            {
                body.Append($"Data prevista di esaurimento: {estimatedRunOutDate.Value.ToString("d", c)}").Append('\n');
            }
            if (administrationSlots is { Count: > 0 } slots)
            {
                body.Append("Posologia:").Append('\n');
                foreach (var slot in slots.OrderBy(s => s.Time.HasValue ? 0 : 1).ThenBy(s => s.Time).ThenBy(s => s.Order))
                {
                    body.Append("  - ").Append(FormatSlotForEmail(slot, medicine.Unit, c)).Append('\n');
                }
            }
            if (!string.IsNullOrWhiteSpace(medicine.DoctorName))
            {
                body.Append($"Medico di riferimento: {medicine.DoctorName}").Append('\n');
            }
            body.Append('\n');
            body.Append("È consigliabile richiedere per tempo una nuova prescrizione al proprio medico.").Append('\n');
            body.Append('\n');
            body.Append("— MedReminder (promemoria organizzativo, non è un dispositivo medico).");
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

    // Toast Windows: usa la lingua SISTEMA (systemLanguageCode).
    // Il chiamante (MedicationMonitor) rileva la lingua sistema con
    // DetectSystemLanguageCode() e la passa qui; il service usa
    // GetIn(languageCode, key) per ignorare la lingua utente.
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

        // Retrocompatibilità: hardcoded IT.
        var titleIt = $"{medicine.Name}: {daysRemaining} giorni residui";
        var bodyIt = $"Quantità stimata per {daysRemaining} giorni. "
                   + "È consigliabile richiedere una nuova prescrizione.";
        return (titleIt, bodyIt);
    }

    // Ritorna il codice lingua ISO 639-1 che corrisponde alla lingua
    // Windows dell'utente (CultureInfo.CurrentUICulture); fallback "en"
    // se la lingua non è tra quelle supportate dall'app. Deve restare
    // allineato con SupportedLanguages.All.
    public static string DetectSystemLanguageCode()
    {
        var twoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return twoLetter switch
        {
            "it" => "it",
            "fr" => "fr",
            "es" => "es",
            _ => "en",
        };
    }
}
