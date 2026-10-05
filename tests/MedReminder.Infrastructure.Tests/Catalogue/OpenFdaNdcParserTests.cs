using System.IO.Compression;
using System.Text;
using FluentAssertions;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Catalogue.Parsers;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Catalogue;

// Exercises the US parser against tests/fixtures/catalogue/fda-ndc-sample.tsv,
// the file scripts/feeds/fda_ndc.py writes for the synthetic openFDA
// sample (8 packages: prescription, OTC, a vaccine, two discontinued
// packages, one product without a brand name).
public sealed class OpenFdaNdcParserTests
{
    private static readonly CountryCode UnitedStates = CountryCode.Parse("US");

    [Fact]
    public async Task Parses_every_package_row_of_the_sample()
    {
        var (rows, report) = await ParseAsync(CatalogueFixtures.BuildFdaNdcSnapshotStream());

        rows.Should().HaveCount(8);
        report.Skipped.Should().Be(0);
        rows.Should().OnlyContain(r => r.Country == UnitedStates);
        rows.Select(r => r.NationalCode).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SupportedCountries_lists_US_only()
    {
        new OpenFdaNdcParser().SupportedCountries.Should().ContainSingle().Which.Value.Should().Be("US");
    }

    [Fact]
    public async Task Maps_the_columns_of_a_prescription_package()
    {
        var (rows, _) = await ParseAsync(CatalogueFixtures.BuildFdaNdcSnapshotStream());

        var row = rows.Single(r => r.NationalCode == "000002-1001-30");
        row.CommercialName.Should().Be("Examplor");
        row.PharmaceuticalForm.Should().Be("TABLET, FILM COATED");
        row.Dosage.Should().Be("10 mg/1 — 30 TABLET, FILM COATED in 1 BOTTLE (0002-1001-30)");
        row.MarketingAuthorisationHolder.Should().Be("Example Labs Inc.");
        row.MarketingStatus.Should().Be("NDA");
        row.DispensingRegime.Should().Be("Rx");
        row.LinkLeaflet.Should().Be(
            "https://dailymed.nlm.nih.gov/dailymed/drugInfo.cfm?setid=c0ffee00-1111-4222-8333-444455556666");
        row.LinkSpc.Should().BeNull();
        row.ActiveIngredients.Should().ContainSingle()
            .Which.Should().Be(new ReferenceActiveIngredientRow("ATORVASTATIN CALCIUM TRIHYDRATE", Atc: null));
    }

    [Fact]
    public async Task Splits_the_ingredients_and_keeps_the_dea_schedule()
    {
        var (rows, _) = await ParseAsync(CatalogueFixtures.BuildFdaNdcSnapshotStream());

        rows.Single(r => r.NationalCode == "060001-1234-01").ActiveIngredients
            .Select(i => i.Name).Should().Equal("HYDROCHLOROTHIAZIDE", "LISINOPRIL");
        rows.Single(r => r.NationalCode == "000409-4888-02").DispensingRegime.Should().Be("Rx, DEA CII");
    }

    [Fact]
    public async Task Keeps_the_discontinued_status_and_has_no_link_without_a_set_id()
    {
        var (rows, _) = await ParseAsync(CatalogueFixtures.BuildFdaNdcSnapshotStream());

        var row = rows.Single(r => r.NationalCode == "060001-1234-01");
        row.MarketingStatus.Should().Be("Discontinued (ANDA, marketing ended 2025-12-31)");
        row.LinkLeaflet.Should().BeNull();
    }

    [Theory]
    [InlineData("c0ffee00-1111-4222-8333-444455556666", "https://dailymed.nlm.nih.gov/dailymed/drugInfo.cfm?setid=c0ffee00-1111-4222-8333-444455556666")]
    [InlineData(" C0FFEE00-1111-4222-8333-444455556666 ", "https://dailymed.nlm.nih.gov/dailymed/drugInfo.cfm?setid=c0ffee00-1111-4222-8333-444455556666")]
    [InlineData("", null)]
    [InlineData("not-a-uuid", null)]
    [InlineData("c0ffee00-1111-4222-8333-444455556666&x=1", null)]
    [InlineData("{c0ffee00-1111-4222-8333-444455556666}", null)]
    public void DailyMed_link_is_built_only_from_a_guid(string setId, string? expected)
    {
        OpenFdaNdcParser.DailyMedLink(setId).Should().Be(expected);
    }

    [Fact]
    public async Task Skips_rows_without_a_canonical_ndc_or_a_name()
    {
        var tsv = new StringBuilder()
            .Append("ndc\tndc_published\tname\tgeneric_name\tform\tdosage\tlabeler\tstatus\tregime\tingredients\tspl_set_id\n")
            .Append("000002-1001-30\t0002-1001-30\tExamplor\t\t\t\t\t\t\t\t\n")
            .Append("0002-1001-30\t0002-1001-30\tTen digits\t\t\t\t\t\t\t\t\n")
            .Append("000002-1001-31\t0002-1001-31\t\t\t\t\t\t\t\t\t\n")
            .Append("000002-1001-32\tshort row\n")
            .ToString();

        var (rows, report) = await ParseAsync(Archive("fda-ndc.tsv", tsv));

        rows.Should().ContainSingle().Which.NationalCode.Should().Be("000002-1001-30");
        report.Skipped.Should().Be(3);
    }

    [Fact]
    public async Task Matches_columns_by_name_in_any_order()
    {
        var tsv = "SPL_SET_ID\tname\tstatus\tndc\tingredients\tregime\tlabeler\tdosage\tform\textra\n" +
                  "\tExamplor\tNDA\t000002-1001-30\tA|B|a\tOTC\tLab\t1 mg\tTABLET\tignored\n";

        var (rows, _) = await ParseAsync(Archive("FDA-NDC.TSV", tsv));

        var row = rows.Should().ContainSingle().Subject;
        row.CommercialName.Should().Be("Examplor");
        row.PharmaceuticalForm.Should().Be("TABLET");
        row.ActiveIngredients.Select(i => i.Name).Should().Equal("A", "B");
    }

    [Fact]
    public async Task A_missing_column_fails_the_import()
    {
        var act = () => ParseAsync(Archive("fda-ndc.tsv", "ndc\tname\n000002-1001-30\tExamplor\n"));

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*missing required column 'form'*");
    }

    [Fact]
    public async Task A_missing_entry_fails_the_import()
    {
        var act = () => ParseAsync(Archive("other.tsv", "ndc\n"));

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*missing required entry 'fda-ndc.tsv'*");
    }

    private static async Task<(List<ReferenceMedicineRow> Rows, ParseReport Report)> ParseAsync(Stream snapshot)
    {
        var report = new ParseReport();
        var rows = new List<ReferenceMedicineRow>();
        await using (snapshot)
        {
            await foreach (var row in new OpenFdaNdcParser().ParseAsync(snapshot, report, CancellationToken.None))
            {
                rows.Add(row);
            }
        }
        return (rows, report);
    }

    private static Stream Archive(string entryName, string content)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        buffer.Position = 0;
        return buffer;
    }
}
