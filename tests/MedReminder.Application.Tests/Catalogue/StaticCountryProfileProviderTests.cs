using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

public sealed class StaticCountryProfileProviderTests
{
    private readonly StaticCountryProfileProvider _sut = new();

    [Theory]
    [InlineData("IT")]
    [InlineData("ES")]
    [InlineData("FR")]
    [InlineData("DE")]
    public void EU_member_countries_get_EU_covered_profile(string country)
    {
        var profile = _sut.GetProfile(CountryCode.Parse(country));

        profile.IncludesEuCentralised.Should().BeTrue();
    }

    [Theory]
    [InlineData("GB")]
    [InlineData("UK")]
    public void UK_variants_are_flagged_as_non_EU_covered(string country)
    {
        var profile = _sut.GetProfile(CountryCode.Parse(country));

        profile.IncludesEuCentralised.Should().BeFalse();
    }

    [Fact]
    public void EU_pseudo_country_is_never_EU_covered_to_avoid_loop()
    {
        var profile = _sut.GetProfile(CountryCode.Parse("EU"));

        profile.IncludesEuCentralised.Should().BeFalse();
    }

    [Fact]
    public void GetSearchScope_for_EU_covered_country_returns_country_then_EU()
    {
        var scope = _sut.GetSearchScope(CountryCode.Parse("IT"));

        scope.Should().Equal(CountryCode.Parse("IT"), CountryCode.Parse("EU"));
    }

    [Fact]
    public void GetSearchScope_for_ES_returns_ES_then_EU()
    {
        // M4 §12.7: Spain is an EU member, so the default
        // IncludesEuCentralised=true applies and the search scope
        // unions the national catalogue with EU-centralised rows.
        var scope = _sut.GetSearchScope(CountryCode.Parse("ES"));

        scope.Should().Equal(CountryCode.Parse("ES"), CountryCode.Parse("EU"));
    }

    [Fact]
    public void GetSearchScope_for_FR_returns_FR_then_EU()
    {
        // M4 §12.7: France is an EU member, so the default
        // IncludesEuCentralised=true applies and the search scope
        // unions the national catalogue with EU-centralised rows.
        var scope = _sut.GetSearchScope(CountryCode.Parse("FR"));

        scope.Should().Equal(CountryCode.Parse("FR"), CountryCode.Parse("EU"));
    }

    [Fact]
    public void GetSearchScope_for_UK_returns_only_UK()
    {
        var scope = _sut.GetSearchScope(CountryCode.Parse("UK"));

        scope.Should().Equal(CountryCode.Parse("UK"));
    }

    [Fact]
    public void GetSearchScope_for_EU_returns_only_EU()
    {
        var scope = _sut.GetSearchScope(CountryCode.Parse("EU"));

        scope.Should().Equal(CountryCode.Parse("EU"));
    }
}
