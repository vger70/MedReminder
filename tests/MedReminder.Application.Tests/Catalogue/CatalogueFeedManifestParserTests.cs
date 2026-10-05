using FluentAssertions;
using MedReminder.Application.Catalogue;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

// The manifest is the `latest.json` each feed script writes under data/<country>/.
public class CatalogueFeedManifestParserTests
{
    private static readonly CatalogueFeedDescriptor Italy = CatalogueFeedDescriptor.Italy;

    private const string Hash = "e4d30a2b06807648425e826f1f3c14b156b615d5b84f42159101a65954588e79";

    [Fact]
    public void Parses_the_published_manifest_and_ignores_unknown_fields()
    {
        const string json = """
            {
              "version": "202609",
              "file": "aifa-202609.zip",
              "generated": "2026-09-29T11:38:01.151381+00:00",
              "csv_count": 3
            }
            """;

        CatalogueFeedManifestParser.TryParse(json, Italy, out var manifest, out var error).Should().BeTrue(error);

        manifest!.Version.Should().Be("202609");
        manifest.File.Should().Be("aifa-202609.zip");
        manifest.Generated.Should().Be(new DateTimeOffset(2026, 9, 29, 11, 38, 1, TimeSpan.Zero).AddTicks(1513810));
        manifest.Sha256.Should().BeNull();
        manifest.Size.Should().BeNull();
    }

    [Fact]
    public void Parses_sha256_and_size_and_lowercases_the_hash()
    {
        var json = $$"""{"version":"202610","sha256":"{{Hash.ToUpperInvariant()}}","size":5016171}""";

        CatalogueFeedManifestParser.TryParse(json, Italy, out var manifest, out _).Should().BeTrue();

        manifest!.Sha256.Should().Be(Hash);
        manifest.Size.Should().Be(5016171);
        manifest.File.Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"version":202609}""")]
    [InlineData("""{"version":"202613"}""")]
    [InlineData("""{"version":"2026-09"}""")]
    [InlineData("""{"version":"202609","file":"aifa-202608.zip"}""")]
    [InlineData("""{"version":"202609","file":"../aifa-202609.zip"}""")]
    [InlineData("""{"version":"202609","sha256":"abc"}""")]
    [InlineData("""{"version":"202609","size":0}""")]
    [InlineData("""{"version":"202609","size":"5016171"}""")]
    [InlineData("""{"version":"202609","country":7}""")]
    [InlineData("""{"version":"202609","country":"Narnia"}""")]
    public void Rejects_invalid_manifests(string json)
    {
        CatalogueFeedManifestParser.TryParse(json, Italy, out var manifest, out var error).Should().BeFalse();

        manifest.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_unparsable_generated_timestamp_is_ignored()
    {
        const string json = """{"version":"202609","generated":"yesterday"}""";

        CatalogueFeedManifestParser.TryParse(json, Italy, out var manifest, out _).Should().BeTrue();

        manifest!.Generated.Should().BeNull();
    }

    [Fact]
    public void Parses_the_country_of_the_per_country_layout()
    {
        const string json = """{"country":"IT","version":"202609","file":"aifa-202609.zip"}""";

        CatalogueFeedManifestParser.TryParse(json, Italy, out var manifest, out var error).Should().BeTrue(error);

        manifest!.Country.Should().Be("IT");
    }

    [Fact]
    public void A_manifest_without_country_has_no_country()
    {
        CatalogueFeedManifestParser.TryParse("""{"version":"202609"}""", Italy, out var manifest, out _).Should().BeTrue();

        manifest!.Country.Should().BeNull();
    }

    [Theory]
    [InlineData("IT", "aifa-202609.zip")]
    [InlineData("EU", "ema-epar-202609.zip")]
    [InlineData("ES", "aemps-202609.zip")]
    [InlineData("FR", "bdpm-202609.zip")]
    [InlineData("US", "fda-ndc-202609.zip")]
    public void Accepts_the_archive_name_and_country_of_each_feed(string country, string file)
    {
        var feed = Descriptor(country);
        var json = $$"""{"country":"{{country}}","version":"202609","file":"{{file}}"}""";

        CatalogueFeedManifestParser.TryParse(json, feed, out var manifest, out var error).Should().BeTrue(error);

        manifest!.File.Should().Be(file);
        manifest.Country.Should().Be(country);
        feed.FileNameFor("202609").Should().Be(file);
    }

    [Theory]
    [InlineData("ES", """{"version":"202609","file":"aifa-202609.zip"}""")]
    [InlineData("FR", """{"version":"202609","file":"bdpm-202608.zip"}""")]
    [InlineData("EU", """{"version":"202609","file":"ema-202609.zip"}""")]
    [InlineData("US", """{"version":"202609","file":"fda-202609.zip"}""")]
    public void Rejects_an_archive_name_of_another_feed(string country, string json)
    {
        CatalogueFeedManifestParser.TryParse(json, Descriptor(country), out var manifest, out var error).Should().BeFalse();

        manifest.Should().BeNull();
        error.Should().Contain("file");
    }

    [Theory]
    [InlineData("IT", "FR")]
    [InlineData("ES", "EU")]
    [InlineData("EU", "IT")]
    public void Rejects_a_manifest_published_for_another_country(string feedCountry, string manifestCountry)
    {
        var json = $$"""{"country":"{{manifestCountry}}","version":"202609"}""";

        CatalogueFeedManifestParser.TryParse(json, Descriptor(feedCountry), out var manifest, out var error).Should().BeFalse();

        manifest.Should().BeNull();
        error.Should().Contain(manifestCountry).And.Contain(feedCountry);
    }

    [Fact]
    public void Accepts_a_manifest_without_country_for_any_feed()
    {
        CatalogueFeedManifestParser.TryParse(
            """{"version":"202609","file":"bdpm-202609.zip"}""", CatalogueFeedDescriptor.France, out var manifest, out _)
            .Should().BeTrue();

        manifest!.Country.Should().BeNull();
    }

    private static CatalogueFeedDescriptor Descriptor(string country) =>
        CatalogueFeedDescriptor.All.Single(feed => feed.Country.Value == country);
}
