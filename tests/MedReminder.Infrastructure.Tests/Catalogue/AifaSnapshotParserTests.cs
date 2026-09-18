using FluentAssertions;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Catalogue.Parsers;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Catalogue;

// Verifies that AifaSnapshotParser honours the import rules in
// ANALYSIS-DRUG-CATALOGUE.md §3.2 against the M0 fixture.
//
// Fixture cardinality (197 data rows in `aifa-confezioni-sample.csv`):
//   TIPO_PROCEDURA distribution: 120 Nazionale, 29 Omeopatico,
//   26 Centralizzata, 19 Mutuo/Decentrata, 3 Importazione Parallela.
// After skipping Omeopatico: 168 medicines kept.
public sealed class AifaSnapshotParserTests
{
    private static readonly CountryCode Italy = CountryCode.Parse("IT");

    [Fact]
    public async Task Parses_expected_row_count_and_records_omeopatico_skips()
    {
        var (rows, report) = await ParseFixtureAsync();

        rows.Should().HaveCount(168);
        report.Skipped.Should().Be(29);
        rows.Should().OnlyContain(r => r.Country == Italy);
    }

    [Fact]
    public async Task Never_emits_Omeopatico_rows()
    {
        var (rows, _) = await ParseFixtureAsync();

        rows.Should().NotContain(r =>
            r.PharmaceuticalForm != null
            && r.PharmaceuticalForm.Equals("Omeopatico", StringComparison.Ordinal));

        // Homeopathic brands from the fixture must be absent.
        rows.Select(r => r.CommercialName)
            .Should().NotContain(new[] { "ASA FOETIDA", "SULFUR", "SILICEA" });
    }

    [Fact]
    public async Task Keeps_Sospesa_and_Procedura_Centralizzata_rows()
    {
        var (rows, _) = await ParseFixtureAsync();

        // ECOSTERIL is Sospesa in the fixture.
        rows.Should().Contain(r => r.CommercialName == "ECOSTERIL");

        // NOVOSEVEN and PUREGON are Procedura Centralizzata but
        // ship with country = IT per §3.2.
        var novoseven = rows.FirstOrDefault(r => r.CommercialName == "NOVOSEVEN");
        novoseven.Should().NotBeNull();
        novoseven!.Country.Should().Be(Italy);
    }

    [Fact]
    public async Task Never_carries_active_ingredient_marked_N_D()
    {
        var (rows, _) = await ParseFixtureAsync();

        rows.Should().NotContain(r =>
            r.ActiveIngredients.Any(a =>
                a.Name.Equals("N.D.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Aspirina_multi_ingredient_row_yields_expected_active_ingredients()
    {
        var (rows, _) = await ParseFixtureAsync();

        var aspirina = rows.FirstOrDefault(r => r.NationalCode == "004763025");
        aspirina.Should().NotBeNull();
        aspirina!.CommercialName.Should().Be("ASPIRINA");
        aspirina.ActiveIngredients.Select(a => a.Name)
            .Should().BeEquivalentTo(new[] { "ACIDO ACETILSALICILICO", "ACIDO ASCORBICO" });

        // ATC is not attached to individual ingredients when the
        // medicine has more than one ingredient (combo ATC).
        aspirina.ActiveIngredients.Should().OnlyContain(a => a.Atc == null);
    }

    [Fact]
    public async Task Single_ingredient_row_receives_the_medicine_ATC_on_its_ingredient()
    {
        var (rows, _) = await ParseFixtureAsync();

        // NOVOSEVEN AIC 029447087 has exactly one active ingredient.
        var novoseven = rows.FirstOrDefault(r => r.NationalCode == "029447087");
        novoseven.Should().NotBeNull();
        novoseven!.ActiveIngredients.Should().ContainSingle();
        novoseven.ActiveIngredients[0].Atc.Should().Be(AtcCode.Parse("B02BD08"));
    }

    [Fact]
    public async Task Populates_dispensing_regime_and_links_from_source_columns()
    {
        var (rows, _) = await ParseFixtureAsync();

        var pipemid = rows.FirstOrDefault(r => r.NationalCode == "023921048");
        pipemid.Should().NotBeNull();
        pipemid!.DispensingRegime.Should().Be("Medicinali soggetti a prescrizione medica");
        pipemid.LinkLeaflet.Should().StartWith("https://api.aifa.gov.it/");
        pipemid.LinkLeaflet.Should().EndWith("ts=FI");
        pipemid.LinkSpc.Should().EndWith("ts=RCP");
    }

    [Fact]
    public async Task Preserves_leading_zeros_in_CODICE_AIC()
    {
        var (rows, _) = await ParseFixtureAsync();

        rows.Select(r => r.NationalCode)
            .Should().OnlyContain(code => code.Length == 9);
    }

    private static async Task<(IReadOnlyList<ReferenceMedicineRow> Rows, ParseReport Report)> ParseFixtureAsync()
    {
        var parser = new AifaSnapshotParser();
        var report = new ParseReport();
        var rows = new List<ReferenceMedicineRow>();
        await using var snapshot = CatalogueFixtures.BuildAifaSnapshotStream();
        await foreach (var row in parser.ParseAsync(snapshot, report, CancellationToken.None))
        {
            rows.Add(row);
        }
        return (rows, report);
    }
}
