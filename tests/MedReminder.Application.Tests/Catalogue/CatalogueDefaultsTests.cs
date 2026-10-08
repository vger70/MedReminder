using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

public sealed class CatalogueDefaultsTests
{
    [Theory]
    [InlineData("IT")]
    [InlineData("ES")]
    [InlineData("FR")]
    [InlineData("US")]
    public void Country_with_a_national_catalogue_gets_that_catalogue(string country)
    {
        CatalogueDefaults.ForCountry(country).Should().Be(CountryCode.Parse(country));
    }

    [Theory]
    [InlineData("DE")]
    [InlineData("AT")]
    [InlineData("GR")]
    [InlineData("IS")]
    [InlineData("LI")]
    [InlineData("NO")]
    public void EU_or_EEA_country_without_a_national_catalogue_gets_EU(string country)
    {
        CatalogueDefaults.ForCountry(country).Should().Be(CountryCode.Parse("EU"));
    }

    [Theory]
    [InlineData("JP")]
    [InlineData("CH")]
    [InlineData("GB")]
    [InlineData("UK")]
    [InlineData("CA")]
    public void Country_outside_the_EU_and_EEA_without_a_national_catalogue_gets_none(string country)
    {
        CatalogueDefaults.ForCountry(country).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ITA")]
    [InlineData("1T")]
    public void Unknown_or_invalid_country_gets_none(string? country)
    {
        CatalogueDefaults.ForCountry(country).Should().BeNull();
    }

    [Fact]
    public void EU_as_country_gets_EU()
    {
        CatalogueDefaults.ForCountry("EU").Should().Be(CountryCode.Parse("EU"));
    }

    [Fact]
    public void Country_code_is_normalised_before_lookup()
    {
        CatalogueDefaults.ForCountry(" de ").Should().Be(CountryCode.Parse("EU"));
        CatalogueDefaults.ForCountry("it").Should().Be(CountryCode.Parse("IT"));
    }

    [Fact]
    public void EL_is_not_treated_as_Greece()
    {
        CatalogueDefaults.IsEuEea(CountryCode.Parse("EL")).Should().BeFalse();
        CatalogueDefaults.ForCountry("EL").Should().BeNull();
    }

    [Fact]
    public void EU_EEA_set_has_27_members_plus_three_EEA_countries()
    {
        var count = Enumerable.Range('A', 26)
            .SelectMany(a => Enumerable.Range('A', 26).Select(b => $"{(char)a}{(char)b}"))
            .Select(CountryCode.Parse)
            .Count(CatalogueDefaults.IsEuEea);

        count.Should().Be(30);
    }

    [Fact]
    public void Available_catalogues_follow_the_remote_feeds()
    {
        CatalogueDefaults.AvailableCatalogues.Should().Equal(
            CountryCode.Parse("IT"),
            CountryCode.Parse("EU"),
            CountryCode.Parse("ES"),
            CountryCode.Parse("FR"),
            CountryCode.Parse("US"));
    }

    [Fact]
    public void Default_catalogue_is_always_available_when_present()
    {
        foreach (var country in new[] { "IT", "DE", "JP", "EU", "NO", "US" })
        {
            var catalogue = CatalogueDefaults.ForCountry(country);
            if (catalogue is { } value)
            {
                CatalogueDefaults.IsAvailable(value).Should().BeTrue();
            }
        }
    }
}
