using System.Globalization;
using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Reporting;

// Produces a textual card of the current therapy to be shown /
// printed for the doctor (Increment 10). No free-form clinical
// information beyond what the user already entered — MedReminder
// stays an organizational reminder.
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

    // Hardcoded English fallback — backwards compatibility with the
    // existing tests that invoke Build(entries, date) without the
    // service. The same labels exist in the JSON dictionaries (keys
    // Reports.Therapy.*) for the localized version.
    private const string HeaderEn = "Therapy card — MedReminder";
    private const string DateLabelEn = "Date: ";
    private const string NoMedicinesEn = "No active medicines.";
    private const string TimesFallbackEn = " × {0} times a day";
    private const string StartLabelEn = "   Start: ";
    private const string EndLabelEn = " · End: ";
    private const string DoctorLabelEn = "   Doctor: ";
    private const string NotesLabelEn = "   Notes: ";
    private const string DisclaimerEn = "— MedReminder: organizational reminder, not a medical device.";

    public static string Build(
        IReadOnlyList<TherapyReportEntry> entries,
        DateOnly reportDate,
        CultureInfo? culture = null,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        // If the service is available, its CurrentCulture has priority
        // for date / number formatting; otherwise honor the caller's
        // explicit culture (or CurrentCulture).
        var c = culture ?? localization?.CurrentCulture ?? CultureInfo.CurrentCulture;

        var sb = new StringBuilder();
        sb.Append(L(localization, "Reports.Therapy.Header", HeaderEn)).Append(NL);
        sb.Append(FormatOrDefault(localization, "Reports.Therapy.Date",
            DateLabelEn + reportDate.ToString("d", c), reportDate.ToString("d", c)))
          .Append(NL).Append(NL);

        var active = entries.Where(e => e.Medicine.IsActive).ToList();
        if (active.Count == 0)
        {
            sb.Append(L(localization, "Reports.Therapy.NoMedicines", NoMedicinesEn)).Append(NL);
            return sb.ToString();
        }

        var i = 1;
        foreach (var entry in active.OrderBy(e => e.Medicine.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            AppendMedicine(sb, i++, entry, c, localization);
            sb.Append(NL);
        }

        sb.Append(L(localization, "Reports.Therapy.Disclaimer", DisclaimerEn));
        return sb.ToString();
    }

    private static void AppendMedicine(
        StringBuilder sb, int index, TherapyReportEntry entry,
        CultureInfo c, ILocalizationService? loc)
    {
        var m = entry.Medicine;
        sb.Append(index).Append(". ").Append(m.Name);
        if (!string.IsNullOrWhiteSpace(m.ActiveIngredient))
        {
            sb.Append(" (").Append(m.ActiveIngredient).Append(')');
        }
        sb.Append(NL);

        if (entry.Slots.Count > 0)
        {
            foreach (var slot in entry.Slots.OrderBy(SortKey))
            {
                sb.Append("   - ").Append(FormatSlot(slot, m.Unit, c)).Append(NL);
            }
        }
        else
        {
            // Legacy dose × frequency fallback.
            var freq = m.AdministrationsPerDay > 0 ? m.AdministrationsPerDay : 1;
            var doseText = m.DosePerAdministration.ToString("0.##", c);
            if (loc is not null)
            {
                // Full key "{0} {1} × {2} times a day".
                sb.Append("   - ").Append(loc.Get("Reports.Therapy.Fallback.Times",
                    doseText, m.Unit, freq)).Append(NL);
            }
            else
            {
                sb.Append("   - ").Append(doseText).Append(' ').Append(m.Unit);
                sb.Append(string.Format(CultureInfo.InvariantCulture, TimesFallbackEn, freq)).Append(NL);
            }
        }

        if (m.StartDate != default)
        {
            var startText = m.StartDate.ToString("d", c);
            if (loc is not null)
            {
                sb.Append("   ").Append(loc.Get("Reports.Therapy.Start", startText));
                if (m.EndDate is { } end)
                {
                    sb.Append(loc.Get("Reports.Therapy.End", end.ToString("d", c)));
                }
            }
            else
            {
                sb.Append(StartLabelEn).Append(startText);
                if (m.EndDate is { } end)
                {
                    sb.Append(EndLabelEn).Append(end.ToString("d", c));
                }
            }
            sb.Append(NL);
        }
        if (!string.IsNullOrWhiteSpace(m.DoctorName))
        {
            if (loc is not null)
            {
                sb.Append("   ").Append(loc.Get("Reports.Therapy.Doctor", m.DoctorName)).Append(NL);
            }
            else
            {
                sb.Append(DoctorLabelEn).Append(m.DoctorName).Append(NL);
            }
        }
        if (!string.IsNullOrWhiteSpace(m.Notes))
        {
            if (loc is not null)
            {
                sb.Append("   ").Append(loc.Get("Reports.Therapy.Notes", m.Notes)).Append(NL);
            }
            else
            {
                sb.Append(NotesLabelEn).Append(m.Notes).Append(NL);
            }
        }
    }

    private static string FormatSlot(MedicationAdministrationSlot slot, string unit, CultureInfo c)
    {
        var doseText = slot.Dose.ToString("0.##", c);
        var sb = new StringBuilder();
        sb.Append(doseText).Append(' ').Append(unit);

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

    // Sorts slots with an explicit time first (chronological order),
    // then the untimed ones (in the order the user declared).
    private static int SortKey(MedicationAdministrationSlot slot)
    {
        if (slot.Time is { } t) return t.Hour * 60 + t.Minute;
        return 10_000 + slot.Order;
    }

    // Helper: uses the service if available, otherwise the fallback.
    private static string L(ILocalizationService? loc, string key, string fallback)
        => loc?.Get(key) ?? fallback;

    // Helper for strings with placeholder {0}: if loc is null, falls
    // back to a default already preformatted by the caller.
    private static string FormatOrDefault(ILocalizationService? loc, string key,
        string fullFallback, params object?[] args)
        => loc?.Get(key, args) ?? fullFallback;
}
