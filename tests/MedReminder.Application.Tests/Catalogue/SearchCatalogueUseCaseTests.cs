using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

public sealed class SearchCatalogueUseCaseTests
{
    private readonly FakeCatalogueQueryService _query = new();
    private readonly StaticCountryProfileProvider _profiles = new();
    private readonly SearchCatalogueUseCase _sut;

    public SearchCatalogueUseCaseTests()
    {
        _sut = new SearchCatalogueUseCase(_query, _profiles);
    }

    [Fact]
    public async Task Italian_user_search_hits_IT_and_EU_scopes()
    {
        _query.CommercialNameResults[("para", ScopeOf("IT", "EU"))] = new[]
        {
            NewRow("Paracetamolo IT", "IT"),
            NewRow("Paracetamolo EU", "EU"),
        };

        var hits = await _sut.SearchByCommercialNameAsync(
            "para", CountryCode.Parse("IT"), CancellationToken.None);

        hits.Select(h => h.CommercialName)
            .Should().BeEquivalentTo(new[] { "Paracetamolo IT", "Paracetamolo EU" });

        _query.LastCommercialNameScope.Should().Equal(
            CountryCode.Parse("IT"), CountryCode.Parse("EU"));
    }

    [Fact]
    public async Task UK_user_search_hits_only_UK_scope()
    {
        _query.CommercialNameResults[("para", ScopeOf("UK"))] = new[]
        {
            NewRow("Panadol UK", "UK"),
        };

        var hits = await _sut.SearchByCommercialNameAsync(
            "para", CountryCode.Parse("UK"), CancellationToken.None);

        hits.Should().ContainSingle().Which.CommercialName.Should().Be("Panadol UK");
        _query.LastCommercialNameScope.Should().Equal(CountryCode.Parse("UK"));
    }

    [Fact]
    public async Task EU_user_search_hits_only_EU_scope()
    {
        _query.CommercialNameResults[("aug", ScopeOf("EU"))] = new[]
        {
            NewRow("Augmentin EU", "EU"),
        };

        var hits = await _sut.SearchByCommercialNameAsync(
            "aug", CountryCode.Parse("EU"), CancellationToken.None);

        hits.Should().ContainSingle().Which.CommercialName.Should().Be("Augmentin EU");
        _query.LastCommercialNameScope.Should().Equal(CountryCode.Parse("EU"));
    }

    [Fact]
    public async Task SearchByActiveIngredient_expands_scope_identically()
    {
        _query.ActiveIngredientResults[("amox", ScopeOf("IT", "EU"))] = new[]
        {
            NewRow("Amoxicillina Teva", "IT"),
        };

        var hits = await _sut.SearchByActiveIngredientAsync(
            "amox", CountryCode.Parse("IT"), CancellationToken.None);

        hits.Should().ContainSingle().Which.CommercialName.Should().Be("Amoxicillina Teva");
        _query.LastActiveIngredientScope.Should().Equal(
            CountryCode.Parse("IT"), CountryCode.Parse("EU"));
    }

    [Fact]
    public async Task DefaultLimit_is_forwarded_to_the_port()
    {
        _query.CommercialNameResults[("para", ScopeOf("IT", "EU"))] = Array.Empty<ReferenceMedicine>();

        await _sut.SearchByCommercialNameAsync(
            "para", CountryCode.Parse("IT"), CancellationToken.None);

        _query.LastCommercialNameLimit.Should().Be(SearchCatalogueUseCase.DefaultLimit);
    }

    [Fact]
    public async Task Explicit_limit_overrides_the_default()
    {
        _query.CommercialNameResults[("para", ScopeOf("IT", "EU"))] = Array.Empty<ReferenceMedicine>();

        await _sut.SearchByCommercialNameAsync(
            "para", CountryCode.Parse("IT"), CancellationToken.None, limit: 5);

        _query.LastCommercialNameLimit.Should().Be(5);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_prefix_is_rejected(string prefix)
    {
        Func<Task> act = () => _sut.SearchByCommercialNameAsync(
            prefix, CountryCode.Parse("IT"), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Non_positive_limit_is_rejected()
    {
        Func<Task> act = () => _sut.SearchByCommercialNameAsync(
            "para", CountryCode.Parse("IT"), CancellationToken.None, limit: 0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // Encodes a scope as an ordered tuple so it can key a Dictionary.
    private static (string Country1, string? Country2) ScopeOf(params string[] countries)
    {
        return countries.Length == 1
            ? (countries[0], null)
            : (countries[0], countries[1]);
    }

    private static ReferenceMedicine NewRow(string name, string country) => new()
    {
        Country = CountryCode.Parse(country),
        NationalCode = Guid.NewGuid().ToString("N")[..9],
        CommercialName = name,
        SnapshotVersion = "202609",
    };

    private sealed class FakeCatalogueQueryService : IReferenceCatalogueQueryService
    {
        public Dictionary<(string, (string, string?)), IReadOnlyList<ReferenceMedicine>>
            CommercialNameResults { get; } = new();

        public Dictionary<(string, (string, string?)), IReadOnlyList<ReferenceMedicine>>
            ActiveIngredientResults { get; } = new();

        public IReadOnlyList<CountryCode> LastCommercialNameScope { get; private set; } = Array.Empty<CountryCode>();

        public IReadOnlyList<CountryCode> LastActiveIngredientScope { get; private set; } = Array.Empty<CountryCode>();

        public int LastCommercialNameLimit { get; private set; }

        public Task<IReadOnlyList<ReferenceMedicine>> SearchByCommercialNameAsync(
            string prefix,
            IReadOnlyCollection<CountryCode> countryScope,
            int limit,
            CancellationToken cancellationToken)
        {
            LastCommercialNameScope = countryScope.ToArray();
            LastCommercialNameLimit = limit;
            var key = (prefix, ScopeKey(countryScope));
            CommercialNameResults.TryGetValue(key, out var rows);
            return Task.FromResult(rows ?? Array.Empty<ReferenceMedicine>());
        }

        public Task<IReadOnlyList<ReferenceMedicine>> SearchByActiveIngredientAsync(
            string prefix,
            IReadOnlyCollection<CountryCode> countryScope,
            int limit,
            CancellationToken cancellationToken)
        {
            LastActiveIngredientScope = countryScope.ToArray();
            var key = (prefix, ScopeKey(countryScope));
            ActiveIngredientResults.TryGetValue(key, out var rows);
            return Task.FromResult(rows ?? Array.Empty<ReferenceMedicine>());
        }

        public Task<ReferenceMedicine?> GetByNationalCodeAsync(
            CountryCode country, string nationalCode, CancellationToken cancellationToken)
            => Task.FromResult<ReferenceMedicine?>(null);

        public Task<IReadOnlyList<CountryCode>> ListAvailableCountriesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CountryCode>>(Array.Empty<CountryCode>());

        private static (string, string?) ScopeKey(IReadOnlyCollection<CountryCode> scope)
        {
            var list = scope.ToList();
            return list.Count == 1
                ? (list[0].Value, null)
                : (list[0].Value, list[1].Value);
        }
    }
}
