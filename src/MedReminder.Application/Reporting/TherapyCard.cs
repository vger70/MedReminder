using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Reporting;

// Structured, renderer-neutral content of the therapy card
// (EVOLUTION-PROPOSALS §4.4). Every string is already localized and
// formatted, so a renderer only lays it out: TherapyReport.RenderText
// produces the plain-text card, the UI draws a paginated table for
// print and PDF. Holds active medicines only.
public sealed record TherapyCard(
    string Title,
    string DateLine,
    string? ProfileLine,
    TherapyCardColumns Columns,
    IReadOnlyList<TherapyCardRow> Rows,
    string EmptyMessage,
    string Disclaimer,
    bool IncludesNotes);

// Localized table headers for the printed layout.
public sealed record TherapyCardColumns(
    string Medicine,
    string Dosage,
    string Period,
    string Doctor,
    string Notes);

// One active medicine. Dates are formatted with the report culture;
// Notes is null when the notes option is off or the medicine has none.
// PeriodLines is the table-cell form of StartDate / EndDate.
public sealed record TherapyCardRow(
    int Index,
    string Name,
    string? ActiveIngredient,
    IReadOnlyList<string> DosageLines,
    string? StartDate,
    string? EndDate,
    IReadOnlyList<string> PeriodLines,
    string? Doctor,
    string? Notes);

// Content choices. Notes are free text and may be private, so they
// are left out unless the caller opts in.
public sealed record TherapyCardOptions(bool IncludeNotes = false);

public static class TherapyCardBuilder
{
    // Hardcoded English fallbacks for callers without a localization
    // service. The same labels exist in the JSON dictionaries (keys
    // Reports.Therapy.*).
    internal const string HeaderEn = "Therapy card — MedReminder";
    internal const string DateLabelEn = "Date: ";
    internal const string ProfileLabelEn = "Profile: ";
    internal const string NoMedicinesEn = "No active medicines.";
    internal const string TimesFallbackEn = " × {0} times a day";
    internal const string DisclaimerEn = "— MedReminder: organizational reminder, not a medical device.";
    private const string ColumnMedicineEn = "Medicine";
    private const string ColumnDosageEn = "Dosage";
    private const string ColumnPeriodEn = "Period";
    private const string ColumnDoctorEn = "Doctor";
    private const string ColumnNotesEn = "Notes";
    private const string PeriodFromEn = "from {0}";
    private const string PeriodUntilEn = "until {0}";

    public static TherapyCard Build(
        IReadOnlyList<TherapyReportEntry> entries,
        DateOnly reportDate,
        TherapyCardOptions? options = null,
        string? profileName = null,
        CultureInfo? culture = null,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        options ??= new TherapyCardOptions();
        // If the service is available, its CurrentCulture has priority
        // for date / number formatting; otherwise honor the caller's
        // explicit culture (or CurrentCulture).
        var c = culture ?? localization?.CurrentCulture ?? CultureInfo.CurrentCulture;
        var loc = localization;

        var dateText = reportDate.ToString("d", c);
        var dateLine = loc?.Get("Reports.Therapy.Date", dateText) ?? DateLabelEn + dateText;
        string? profileLine = null;
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            profileLine = loc?.Get("Reports.Therapy.Profile", profileName) ?? ProfileLabelEn + profileName;
        }

        var columns = new TherapyCardColumns(
            Medicine: loc?.Get("Reports.Therapy.Column.Medicine") ?? ColumnMedicineEn,
            Dosage: loc?.Get("Reports.Therapy.Column.Dosage") ?? ColumnDosageEn,
            Period: loc?.Get("Reports.Therapy.Column.Period") ?? ColumnPeriodEn,
            Doctor: loc?.Get("Reports.Therapy.Column.Doctor") ?? ColumnDoctorEn,
            Notes: loc?.Get("Reports.Therapy.Column.Notes") ?? ColumnNotesEn);

        var rows = new List<TherapyCardRow>();
        var index = 1;
        foreach (var entry in entries
            .Where(e => e.Medicine.IsActive)
            .OrderBy(e => e.Medicine.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            rows.Add(BuildRow(index++, entry, options, c, loc));
        }

        return new TherapyCard(
            Title: loc?.Get("Reports.Therapy.Header") ?? HeaderEn,
            DateLine: dateLine,
            ProfileLine: profileLine,
            Columns: columns,
            Rows: rows,
            EmptyMessage: loc?.Get("Reports.Therapy.NoMedicines") ?? NoMedicinesEn,
            Disclaimer: loc?.Get("Reports.Therapy.Disclaimer") ?? DisclaimerEn,
            IncludesNotes: options.IncludeNotes);
    }

    private static TherapyCardRow BuildRow(
        int index, TherapyReportEntry entry, TherapyCardOptions options,
        CultureInfo c, ILocalizationService? loc)
    {
        var m = entry.Medicine;

        var dosage = new List<string>();
        if (entry.Slots.Count > 0)
        {
            foreach (var slot in entry.Slots.OrderBy(SortKey))
            {
                dosage.Add(FormatSlot(slot, m.Unit, c, loc));
            }
        }
        else
        {
            // Legacy dose × frequency fallback.
            var freq = m.AdministrationsPerDay > 0 ? m.AdministrationsPerDay : 1;
            var doseText = m.DosePerAdministration.ToString("0.##", c);
            dosage.Add(loc?.Get("Reports.Therapy.Fallback.Times", doseText, m.Unit, freq)
                ?? doseText + " " + m.Unit + string.Format(CultureInfo.InvariantCulture, TimesFallbackEn, freq));
        }

        string? start = null;
        string? end = null;
        var period = new List<string>();
        if (m.StartDate != default)
        {
            start = m.StartDate.ToString("d", c);
            period.Add(loc?.Get("Reports.Therapy.Period.From", start)
                ?? string.Format(c, PeriodFromEn, start));
            if (m.EndDate is { } e)
            {
                end = e.ToString("d", c);
                period.Add(loc?.Get("Reports.Therapy.Period.Until", end)
                    ?? string.Format(c, PeriodUntilEn, end));
            }
        }

        return new TherapyCardRow(
            Index: index,
            Name: m.Name,
            ActiveIngredient: string.IsNullOrWhiteSpace(m.ActiveIngredient) ? null : m.ActiveIngredient,
            DosageLines: dosage,
            StartDate: start,
            EndDate: end,
            PeriodLines: period,
            Doctor: string.IsNullOrWhiteSpace(m.DoctorName) ? null : m.DoctorName,
            Notes: options.IncludeNotes && !string.IsNullOrWhiteSpace(m.Notes) ? m.Notes : null);
    }

    private static string FormatSlot(
        MedicationAdministrationSlot slot, string unit, CultureInfo c, ILocalizationService? loc)
    {
        var text = slot.Dose.ToString("0.##", c) + " " + unit;
        if (!string.IsNullOrWhiteSpace(slot.TimingLabel))
        {
            text += " " + slot.TimingLabel;
        }
        if (slot.Time is { } t)
        {
            text += " (" + t.ToString("HH:mm", c) + ")";
        }
        return text + SlotTexts.AsNeededSuffix(slot, loc);
    }

    // Sorts slots with an explicit time first (chronological order),
    // then the untimed ones (in the order the user declared).
    private static int SortKey(MedicationAdministrationSlot slot)
    {
        if (slot.Time is { } t) return t.Hour * 60 + t.Minute;
        return 10_000 + slot.Order;
    }
}
