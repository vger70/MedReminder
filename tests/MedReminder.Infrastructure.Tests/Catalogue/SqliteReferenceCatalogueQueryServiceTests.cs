using FluentAssertions;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Catalogue;
using MedReminder.Infrastructure.Catalogue.Parsers;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Catalogue;

// Exercises the SQLite query service against the fully-imported M0
// fixture (168 Italian medicines). Because the fixture is 100%
// Italian, the "EU" scope always yields an empty result — matches
// what §3.2 M1 documents.
public sealed class SqliteReferenceCatalogueQueryServiceTests : IAsyncLifetime
{
    private static readonly CountryCode Italy = CountryCode.Parse("IT");
    private static readonly CountryCode EU = CountryCode.Parse("EU");

    private readonly SqliteInMemoryFixture _fixture = new();

    public async Task InitializeAsync()
    {
        var importer = new CsvReferenceCatalogueImporter(
            _fixture.CreateContext(),
            new IReferenceSnapshotParser[] { new AifaSnapshotParser() },
            TimeProvider.System);
        await using var snapshot = CatalogueFixtures.BuildAifaSnapshotStream();
        await importer.ImportAsync(snapshot, Italy, "202609", CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        _fixture.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SearchByCommercialName_asp_returns_Aspirina_rows_for_IT_and_EU_scope()
    {
        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        var hits = await sut.SearchByCommercialNameAsync(
            prefix: "asp",
            countryScope: new[] { Italy, EU },
            limit: 20,
            cancellationToken: CancellationToken.None);

        hits.Should().NotBeEmpty();
        hits.Select(h => h.CommercialName).Distinct()
            .Should().Contain("ASPIRINA");
        hits.Should().OnlyContain(h => h.CommercialName.StartsWith("ASP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchByCommercialName_hydrates_active_ingredients_for_multi_ingredient_rows()
    {
        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        var hits = await sut.SearchByCommercialNameAsync(
            prefix: "aspirina", countryScope: new[] { Italy }, limit: 5, CancellationToken.None);

        hits.Should().NotBeEmpty();
        var aspirina = hits.First(h => h.NationalCode == "004763025");
        aspirina.ActiveIngredients.Select(i => i.Name)
            .Should().BeEquivalentTo(new[] { "ACIDO ACETILSALICILICO", "ACIDO ASCORBICO" });
    }

    [Fact]
    public async Task SearchByCommercialName_returns_no_rows_for_EU_only_scope_on_Italian_fixture()
    {
        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        var hits = await sut.SearchByCommercialNameAsync(
            prefix: "asp", countryScope: new[] { EU }, limit: 20, CancellationToken.None);

        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task Limit_bounds_the_number_of_rows_returned()
    {
        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        var hits = await sut.SearchByCommercialNameAsync(
            prefix: "a", countryScope: new[] { Italy }, limit: 3, CancellationToken.None);

        hits.Should().HaveCount(3);
    }

    [Fact]
    public async Task SearchByActiveIngredient_finds_medicines_via_the_M2M()
    {
        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        var hits = await sut.SearchByActiveIngredientAsync(
            prefix: "acido acetilsalicilico",
            countryScope: new[] { Italy },
            limit: 20,
            cancellationToken: CancellationToken.None);

        hits.Should().NotBeEmpty();
        hits.Select(h => h.CommercialName).Should().Contain("ASPIRINA");
    }

    [Fact]
    public async Task GetByNationalCode_returns_the_medicine_with_its_active_ingredients()
    {
        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        var hit = await sut.GetByNationalCodeAsync(Italy, "004763025", CancellationToken.None);

        hit.Should().NotBeNull();
        hit!.CommercialName.Should().Be("ASPIRINA");
        hit.ActiveIngredients.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByNationalCode_returns_null_for_unknown_code()
    {
        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        var hit = await sut.GetByNationalCodeAsync(Italy, "999999999", CancellationToken.None);

        hit.Should().BeNull();
    }

    [Fact]
    public async Task Search_normalises_case_and_diacritics_in_the_input_prefix()
    {
        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        var hits = await sut.SearchByCommercialNameAsync(
            prefix: "ÀSpÍrÌnÀ",
            countryScope: new[] { Italy },
            limit: 5,
            cancellationToken: CancellationToken.None);

        hits.Select(h => h.CommercialName).Should().Contain("ASPIRINA");
    }

    // --- M3: cross-country search after loading both catalogues -----

    [Fact]
    public async Task Search_from_userCountry_IT_returns_IT_and_EU_rows_after_loading_EU()
    {
        var importer = new CsvReferenceCatalogueImporter(
            _fixture.CreateContext(),
            new IReferenceSnapshotParser[] { new AifaSnapshotParser(), new EmaEparParser() },
            TimeProvider.System);
        await using (var eu = CatalogueFixtures.BuildEmaEparSnapshotStream())
        {
            await importer.ImportAsync(eu, EU, "202609", CancellationToken.None);
        }

        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        // Symtuza (EU-only) starts with 'sy'; an Italian user with
        // scope { IT, EU } must see it — even though the Italian
        // fixture never contains it.
        var hits = await sut.SearchByCommercialNameAsync(
            prefix: "sym",
            countryScope: new[] { Italy, EU },
            limit: 20,
            cancellationToken: CancellationToken.None);

        hits.Should().NotBeEmpty();
        hits.Select(h => h.CommercialName).Should().Contain("Symtuza");
        hits.Should().OnlyContain(h => h.Country.Value == "IT" || h.Country.Value == "EU");
    }

    [Fact]
    public async Task Search_from_userCountry_EU_returns_only_EU_rows_after_loading_EU()
    {
        var importer = new CsvReferenceCatalogueImporter(
            _fixture.CreateContext(),
            new IReferenceSnapshotParser[] { new AifaSnapshotParser(), new EmaEparParser() },
            TimeProvider.System);
        await using (var eu = CatalogueFixtures.BuildEmaEparSnapshotStream())
        {
            await importer.ImportAsync(eu, EU, "202609", CancellationToken.None);
        }

        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        // Prefix "a" would match dozens of Italian rows too, but with
        // scope { EU } only supranational rows are returned.
        var hits = await sut.SearchByCommercialNameAsync(
            prefix: "a",
            countryScope: new[] { EU },
            limit: 50,
            cancellationToken: CancellationToken.None);

        hits.Should().NotBeEmpty();
        hits.Should().OnlyContain(h => h.Country.Value == "EU");
    }

    [Fact]
    public async Task Available_countries_includes_both_IT_and_EU_after_loading_EU()
    {
        var importer = new CsvReferenceCatalogueImporter(
            _fixture.CreateContext(),
            new IReferenceSnapshotParser[] { new AifaSnapshotParser(), new EmaEparParser() },
            TimeProvider.System);
        await using (var eu = CatalogueFixtures.BuildEmaEparSnapshotStream())
        {
            await importer.ImportAsync(eu, EU, "202609", CancellationToken.None);
        }

        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());
        var countries = await sut.ListAvailableCountriesAsync(CancellationToken.None);

        countries.Select(c => c.Value).Should().Contain(new[] { "IT", "EU" });
    }

    // --- M4: ES + FR cross-country search ---------------------------

    private static readonly CountryCode Spain = CountryCode.Parse("ES");
    private static readonly CountryCode France = CountryCode.Parse("FR");

    [Fact]
    public async Task Search_from_userCountry_ES_returns_ES_and_EU_rows_after_loading_ES_and_EU()
    {
        var importer = new CsvReferenceCatalogueImporter(
            _fixture.CreateContext(),
            new IReferenceSnapshotParser[]
            {
                new AifaSnapshotParser(),
                new EmaEparParser(),
                new AempsCimaParser(),
                new AnsmBdpmParser(),
            },
            TimeProvider.System);
        await using (var eu = CatalogueFixtures.BuildEmaEparSnapshotStream())
        {
            await importer.ImportAsync(eu, EU, "202609", CancellationToken.None);
        }
        await using (var spain = CatalogueFixtures.BuildAempsSnapshotStream())
        {
            await importer.ImportAsync(spain, Spain, "202609", CancellationToken.None);
        }

        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        // The AEMPS and EPAR fixtures are disjoint on any short prefix
        // (the curated CIMA sample and the curated EMA sample never
        // overlap on the same starting letters at fixture size), so a
        // single query cannot demonstrate both country codes in the
        // result set without either widening the limit past the row
        // count or picking a fragile prefix. Instead we hit the scope
        // twice — once with a prefix present in the ES fixture and
        // once with a prefix present only in the EU fixture — proving
        // that the scope `{ ES, EU }` lets each side through.

        var esHits = await sut.SearchByCommercialNameAsync(
            prefix: "amo",
            countryScope: new[] { Spain, EU },
            limit: 20,
            cancellationToken: CancellationToken.None);
        esHits.Should().NotBeEmpty();
        esHits.Should().OnlyContain(h => h.Country.Value == "ES" || h.Country.Value == "EU");
        esHits.Select(h => h.Country.Value).Should().Contain("ES");

        var euHits = await sut.SearchByCommercialNameAsync(
            prefix: "sym",
            countryScope: new[] { Spain, EU },
            limit: 20,
            cancellationToken: CancellationToken.None);
        euHits.Should().NotBeEmpty();
        euHits.Should().OnlyContain(h => h.Country.Value == "ES" || h.Country.Value == "EU");
        euHits.Select(h => h.Country.Value).Should().Contain("EU");
    }

    [Fact]
    public async Task Search_from_userCountry_FR_returns_FR_and_EU_rows_after_loading_FR_and_EU()
    {
        var importer = new CsvReferenceCatalogueImporter(
            _fixture.CreateContext(),
            new IReferenceSnapshotParser[]
            {
                new AifaSnapshotParser(),
                new EmaEparParser(),
                new AempsCimaParser(),
                new AnsmBdpmParser(),
            },
            TimeProvider.System);
        await using (var eu = CatalogueFixtures.BuildEmaEparSnapshotStream())
        {
            await importer.ImportAsync(eu, EU, "202609", CancellationToken.None);
        }
        await using (var france = CatalogueFixtures.BuildBdpmSnapshotStream())
        {
            await importer.ImportAsync(france, France, "202609", CancellationToken.None);
        }

        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());

        // See the ES-side test for why we run two prefix probes here
        // instead of a single broad query. BDPM in particular has
        // ~60 rows starting with "A" in the fixture, which would
        // saturate a limit-50 mixed-scope query before any EPAR "A"
        // row (Abrysvo, Aybintio) could enter the result set. The
        // two-probe pattern proves scope { FR, EU } lets each side
        // through without depending on how alphabetical ordering
        // interacts with the fixture's row density.

        var frHits = await sut.SearchByCommercialNameAsync(
            prefix: "amox",
            countryScope: new[] { France, EU },
            limit: 20,
            cancellationToken: CancellationToken.None);
        frHits.Should().NotBeEmpty();
        frHits.Should().OnlyContain(h => h.Country.Value == "FR" || h.Country.Value == "EU");
        frHits.Select(h => h.Country.Value).Should().Contain("FR");

        var euHits = await sut.SearchByCommercialNameAsync(
            prefix: "sym",
            countryScope: new[] { France, EU },
            limit: 20,
            cancellationToken: CancellationToken.None);
        euHits.Should().NotBeEmpty();
        euHits.Should().OnlyContain(h => h.Country.Value == "FR" || h.Country.Value == "EU");
        euHits.Select(h => h.Country.Value).Should().Contain("EU");
    }

    [Fact]
    public async Task Available_countries_lists_all_four_after_loading_IT_EU_ES_FR()
    {
        var importer = new CsvReferenceCatalogueImporter(
            _fixture.CreateContext(),
            new IReferenceSnapshotParser[]
            {
                new AifaSnapshotParser(),
                new EmaEparParser(),
                new AempsCimaParser(),
                new AnsmBdpmParser(),
            },
            TimeProvider.System);
        await using (var eu = CatalogueFixtures.BuildEmaEparSnapshotStream())
        {
            await importer.ImportAsync(eu, EU, "202609", CancellationToken.None);
        }
        await using (var spain = CatalogueFixtures.BuildAempsSnapshotStream())
        {
            await importer.ImportAsync(spain, Spain, "202609", CancellationToken.None);
        }
        await using (var france = CatalogueFixtures.BuildBdpmSnapshotStream())
        {
            await importer.ImportAsync(france, France, "202609", CancellationToken.None);
        }

        var sut = new SqliteReferenceCatalogueQueryService(_fixture.CreateContext());
        var countries = await sut.ListAvailableCountriesAsync(CancellationToken.None);

        countries.Select(c => c.Value).Should().Contain(new[] { "IT", "EU", "ES", "FR" });
    }
}
