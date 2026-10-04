using FluentAssertions;
using MedReminder.Application.Coverage;
using MedReminder.Application.Reporting;
using MedReminder.Application.Tests.Support;
using Xunit;

namespace MedReminder.Application.Tests.Coverage;

// Localized content of the coverage plan: every language defines its
// keys, and the table and its text form carry the computed values.
public class CoveragePlanTextTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static CoverageRow Row(string name, CoverageStatus status, decimal needed = 28m,
        decimal stockAtStart = 20m, decimal shortfall = 8m, decimal? package = 28m, int? packages = 1)
        => new(Guid.NewGuid(), name, "enalapril", "tablets", stockAtStart, stockAtStart, needed, shortfall,
            package, packages, status);

    private static CoveragePlan Plan() => new(Today, Today, Today.AddDays(13),
    [
        Row("Enalapril", CoverageStatus.Short),
        Row("Aspirin", CoverageStatus.Covered, needed: 14m, stockAtStart: 30m, shortfall: 0m, packages: null),
        Row("Paracetamol", CoverageStatus.AsNeeded, needed: 0m, shortfall: 0m, packages: null),
        Row("Later", CoverageStatus.NotInUse, needed: 0m, stockAtStart: 0m, shortfall: 0m, packages: null),
    ]);

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Every_language_defines_the_texts_of_the_plan(string language)
    {
        var loc = new JsonDictionaryLocalizationService(language);

        var table = CoveragePlanText.BuildTable(Plan(), "Anna", loc);
        var text = PrintableTableText.Render(table);
        _ = CoveragePlanText.Summary(Plan(), loc);

        loc.FallbackHits.Should().BeEmpty();
        text.Should().NotContain("[Reports.").And.NotContain("[Common.");
    }

    [Fact]
    public void The_table_carries_the_values_of_each_row()
    {
        var loc = new JsonDictionaryLocalizationService("en");

        var table = CoveragePlanText.BuildTable(Plan(), "Anna", loc);

        table.Columns.Should().HaveCount(5);
        table.HeaderLines.Should().Contain("Profile: Anna");
        table.HeaderLines.Should().Contain(l => l.Contains("(14 days)"));
        table.Rows[0].Cells.Should().Equal("Enalapril", "28 tablets", "20 tablets", "8 tablets", "1 × 28 tablets");
        table.Rows[0].Detail.Should().Be("enalapril");
        table.Rows[1].Cells[3].Should().Be("covered");
        table.Rows[2].Cells[1].Should().Be("as needed: not computed");
        table.Rows[3].Cells[1].Should().Be("not taken in the period");
        table.Rows[3].Cells[2].Should().Be("runs out before");
    }

    [Fact]
    public void The_summary_counts_the_medicines_not_covered()
    {
        var loc = new JsonDictionaryLocalizationService("en");

        CoveragePlanText.Summary(Plan(), loc).Should().Be("Medicines not covered for the period: 1 of 4.");
        CoveragePlanText.Summary(new CoveragePlan(Today, Today, Today, [Row("A", CoverageStatus.Covered)]), loc)
            .Should().Be("The stock covers the whole period.");
    }

    [Fact]
    public void The_text_form_lists_every_row_with_its_column_headers()
    {
        var table = new PrintableTable("Title", ["Line"],
            [new PrintableColumn("Medicine", 1f), new PrintableColumn("Needed", 1f)],
            [new PrintableRow(["Enalapril", "28\nmore"], "enalapril")], "None", "Disclaimer");

        PrintableTableText.Render(table, "\n").Should().Be(
            "Title\nLine\n\n1. Enalapril (enalapril)\n   Needed: 28; more\n\nDisclaimer");
        PrintableTableText.Render(table with { Rows = [] }, "\n").Should().Be("Title\nLine\n\nNone\n");
    }
}
