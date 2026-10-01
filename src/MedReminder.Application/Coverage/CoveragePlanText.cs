using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Reporting;

namespace MedReminder.Application.Coverage;

// Localized, formatted content of a coverage plan: the printed table
// (PrintableTable) and the one-line summary the dialog shows. Quantities
// carry the medicine's unit; dates use the app culture.
public static class CoveragePlanText
{
    public static PrintableTable BuildTable(CoveragePlan plan, string? profileName, ILocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(loc);
        var c = loc.CurrentCulture;

        var header = new List<string>();
        if (!string.IsNullOrWhiteSpace(profileName)) header.Add(loc.Get("Reports.Therapy.Profile", profileName));
        header.Add(loc.Get("Reports.Therapy.Date", plan.Today.ToString("d", c)));
        header.Add(loc.Get("Reports.Coverage.Period",
            plan.From.ToString("d", c), plan.To.ToString("d", c), plan.Days));
        header.Add(loc.Get("Reports.Coverage.Note"));

        var columns = new[]
        {
            new PrintableColumn(loc.Get("Reports.Coverage.Column.Medicine"), 0.30f),
            new PrintableColumn(loc.Get("Reports.Coverage.Column.Needed"), 0.17f),
            new PrintableColumn(loc.Get("Reports.Coverage.Column.StockAtStart"), 0.17f),
            new PrintableColumn(loc.Get("Reports.Coverage.Column.Missing"), 0.17f),
            new PrintableColumn(loc.Get("Reports.Coverage.Column.Packages"), 0.19f),
        };

        var rows = plan.Rows.Select(r => new PrintableRow(
            [r.Name, Needed(r, loc, c), StockAtStart(r, loc, c), Missing(r, loc, c), Packages(r, loc, c)],
            string.IsNullOrWhiteSpace(r.ActiveIngredient) ? null : r.ActiveIngredient)).ToList();

        return new PrintableTable(
            loc.Get("Reports.Coverage.Title"),
            header,
            columns,
            rows,
            loc.Get("Reports.Therapy.NoMedicines"),
            loc.Get("Reports.Therapy.Disclaimer"));
    }

    public static string Summary(CoveragePlan plan, ILocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(loc);
        if (plan.Rows.Count == 0) return loc.Get("Reports.Therapy.NoMedicines");
        return plan.ShortCount == 0
            ? loc.Get("Reports.Coverage.Summary.AllCovered")
            : loc.Get("Reports.Coverage.Summary.Short", plan.ShortCount, plan.Rows.Count);
    }

    public static string Needed(CoverageRow row, ILocalizationService loc, CultureInfo c) => row.Status switch
    {
        CoverageStatus.AsNeeded => loc.Get("Reports.Coverage.AsNeeded"),
        CoverageStatus.NotInUse => loc.Get("Reports.Coverage.NotInUse"),
        _ => Quantity(row.Needed, row.Unit, loc, c),
    };

    public static string StockAtStart(CoverageRow row, ILocalizationService loc, CultureInfo c)
        => row.StockAtStart > 0m
            ? Quantity(row.StockAtStart, row.Unit, loc, c)
            : loc.Get("Reports.Coverage.RunsOutBefore");

    public static string Missing(CoverageRow row, ILocalizationService loc, CultureInfo c) => row.Status switch
    {
        CoverageStatus.Short => Quantity(row.Shortfall, row.Unit, loc, c),
        CoverageStatus.Covered => loc.Get("Reports.Coverage.Covered"),
        _ => loc.Get("Common.NotAvailable"),
    };

    public static string Packages(CoverageRow row, ILocalizationService loc, CultureInfo c)
        => row is { Packages: { } count, PackageQuantity: { } size }
            ? loc.Get("Reports.Coverage.Packages", count, Number(size, c), row.Unit)
            : loc.Get("Common.NotAvailable");

    private static string Quantity(decimal value, string unit, ILocalizationService loc, CultureInfo c)
        => loc.Get("Reports.Coverage.Quantity", Number(value, c), unit);

    private static string Number(decimal value, CultureInfo c) => value.ToString("0.##", c);
}
