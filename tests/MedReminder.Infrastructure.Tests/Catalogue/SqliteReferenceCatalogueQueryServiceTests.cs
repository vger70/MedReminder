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
}
