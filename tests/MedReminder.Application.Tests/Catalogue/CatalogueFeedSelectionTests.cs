using FluentAssertions;
using MedReminder.Application.Catalogue;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

// Decision D4 of ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md: the
// reference country's feed plus EU, in IT, EU, ES, FR order.
public class CatalogueFeedSelectionTests
{
    [Theory]
    [InlineData("IT", "IT,EU")]
    [InlineData("it", "IT,EU")]
    [InlineData("ES", "EU,ES")]
    [InlineData("FR", "EU,FR")]
    [InlineData("EU", "EU")]
    [InlineData("European Union", "EU")]
    [InlineData("DE", "EU")]
    [InlineData("GB", "EU")]
    [InlineData("", "IT,EU")]
    [InlineData(null, "IT,EU")]
    [InlineData("Italia", "IT,EU")]
    public void Selects_the_reference_country_and_EU(string? reference, string expected)
    {
        var selected = CatalogueFeedSelection.Select(reference, AllEnabled());

        Codes(selected).Should().Be(expected);
    }

    [Theory]
    [InlineData("IT", "IT,EU")]
    [InlineData("ES", "EU,ES")]
    [InlineData("FR", "EU,FR")]
    [InlineData("EU", "EU")]
    public void The_defaults_enable_every_published_feed(string reference, string expected)
    {
        var selected = CatalogueFeedSelection.Select(reference, new CatalogueFeedOptions());

        Codes(selected).Should().Be(expected);
    }

    [Fact]
    public void Skips_disabled_feeds()
    {
        var options = AllEnabled();
        options.Feeds["EU"].Enabled = false;

        Codes(CatalogueFeedSelection.Select("ES", options)).Should().Be("ES");
    }

    [Fact]
    public void A_feed_missing_from_the_configuration_is_off()
    {
        var options = AllEnabled();
        options.Feeds.Remove("FR");

        Codes(CatalogueFeedSelection.Select("FR", options)).Should().Be("EU");
    }

    [Fact]
    public void Feed_keys_are_case_insensitive()
    {
        var options = new CatalogueFeedOptions();
        options.Feeds["eu"].Enabled = false;

        Codes(CatalogueFeedSelection.Select("ES", options)).Should().Be("ES");
    }

    private static CatalogueFeedOptions AllEnabled()
    {
        var options = new CatalogueFeedOptions { Enabled = true };
        foreach (var feed in options.Feeds.Values)
        {
            feed.Enabled = true;
        }
        return options;
    }

    private static string Codes(IEnumerable<CatalogueFeedDescriptor> feeds) =>
        string.Join(",", feeds.Select(feed => feed.Country.Value));
}
