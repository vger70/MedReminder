using System.Globalization;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Notifications;

// Formatta i testi (subject/body per email, title/body per toast) a
// partire dallo stato della medicina. Nessuna indicazione clinica
// (spec §22): solo identificazione della medicina + invito a richiedere
// prescrizione.
public static class NotificationTexts
{
    public static EmailMessage BuildEmail(
        Medicine medicine,
        decimal currentStock,
        int daysRemaining,
        DateOnly? estimatedRunOutDate,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        var c = culture ?? CultureInfo.CurrentCulture;

        var subject = $"MedReminder — {medicine.Name} in esaurimento ({daysRemaining} gg)";

        var body = new System.Text.StringBuilder();
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
        if (!string.IsNullOrWhiteSpace(medicine.DoctorName))
        {
            body.Append($"Medico di riferimento: {medicine.DoctorName}").Append('\n');
        }
        body.Append('\n');
        body.Append("È consigliabile richiedere per tempo una nuova prescrizione al proprio medico.").Append('\n');
        body.Append('\n');
        body.Append("— MedReminder (promemoria organizzativo, non è un dispositivo medico).");

        return new EmailMessage(subject, body.ToString());
    }

    public static (string Title, string Body) BuildToast(
        Medicine medicine,
        int daysRemaining,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        _ = culture;

        var title = $"{medicine.Name}: {daysRemaining} giorni residui";
        var body = $"Quantità stimata per {daysRemaining} giorni. "
                 + "È consigliabile richiedere una nuova prescrizione.";
        return (title, body);
    }
}
