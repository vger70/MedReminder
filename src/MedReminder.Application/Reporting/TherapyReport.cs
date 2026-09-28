using System.Globalization;
using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Reporting;

// Produces a textual card of the current therapy to be shown /
// copied / saved for the doctor (Increment 10). No free-form clinical
// information beyond what the user already entered — MedReminder
// stays an organizational reminder.
//
// The content comes from TherapyCardBuilder; this class is the
// plain-text renderer of that model. The printed / PDF table is drawn
// by the UI from the same model.
//
// Uses Environment.NewLine (= "\r\n" on Windows) instead of '\n'
// because the WinForms multiline TextBox only renders a new line on
// CRLF; with LF everything is printed on a single line.
public sealed record TherapyReportEntry(
    Medicine Medicine,
    IReadOnlyList<MedicationAdministrationSlot> Slots);

public static class TherapyReport
{
    private static readonly string NL = Environment.NewLine;

    // Hardcoded English fallback — used when no localization service
    // is passed. The same labels exist in the JSON dictionaries (keys
    // Reports.Therapy.*) for the localized version.
    private const string StartLabelEn = "   Start: ";
    private const string EndLabelEn = " · End: ";
    private const string DoctorLabelEn = "   Doctor: ";
    private const string NotesLabelEn = "   Notes: ";

    // Legacy entry point: same output as before the card model, notes
    // included when present. Callers that expose the notes choice to
    // the user build the card with explicit options and call
    // RenderText.
    public static string Build(
        IReadOnlyList<TherapyReportEntry> entries,
        DateOnly reportDate,
        CultureInfo? culture = null,
        ILocalizationService? localization = null)
    {
        var card = TherapyCardBuilder.Build(
            entries, reportDate, new TherapyCardOptions(IncludeNotes: true),
            profileName: null, culture, localization);
        return RenderText(card, localization);
    }

    public static string RenderText(TherapyCard card, ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        var loc = localization;

        var sb = new StringBuilder();
        sb.Append(card.Title).Append(NL);
        if (card.ProfileLine is not null)
        {
            sb.Append(card.ProfileLine).Append(NL);
        }
        sb.Append(card.DateLine).Append(NL).Append(NL);

        if (card.Rows.Count == 0)
        {
            sb.Append(card.EmptyMessage).Append(NL);
            return sb.ToString();
        }

        foreach (var row in card.Rows)
        {
            AppendRow(sb, row, loc);
            sb.Append(NL);
        }

        sb.Append(card.Disclaimer);
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, TherapyCardRow row, ILocalizationService? loc)
    {
        sb.Append(row.Index).Append(". ").Append(row.Name);
        if (row.ActiveIngredient is not null)
        {
            sb.Append(" (").Append(row.ActiveIngredient).Append(')');
        }
        sb.Append(NL);

        foreach (var line in row.DosageLines)
        {
            sb.Append("   - ").Append(line).Append(NL);
        }

        if (row.StartDate is not null)
        {
            if (loc is not null)
            {
                sb.Append("   ").Append(loc.Get("Reports.Therapy.Start", row.StartDate));
                if (row.EndDate is not null)
                {
                    sb.Append(loc.Get("Reports.Therapy.End", row.EndDate));
                }
            }
            else
            {
                sb.Append(StartLabelEn).Append(row.StartDate);
                if (row.EndDate is not null)
                {
                    sb.Append(EndLabelEn).Append(row.EndDate);
                }
            }
            sb.Append(NL);
        }
        if (row.Doctor is not null)
        {
            if (loc is not null)
            {
                sb.Append("   ").Append(loc.Get("Reports.Therapy.Doctor", row.Doctor)).Append(NL);
            }
            else
            {
                sb.Append(DoctorLabelEn).Append(row.Doctor).Append(NL);
            }
        }
        if (row.Notes is not null)
        {
            if (loc is not null)
            {
                sb.Append("   ").Append(loc.Get("Reports.Therapy.Notes", row.Notes)).Append(NL);
            }
            else
            {
                sb.Append(NotesLabelEn).Append(row.Notes).Append(NL);
            }
        }
    }
}
