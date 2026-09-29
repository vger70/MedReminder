using FluentAssertions;
using MedReminder.Application.Catalogue;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

// The manifest is the `latest.json` written by scripts/download_aifa.py.
public class CatalogueFeedManifestParserTests
{
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

        CatalogueFeedManifestParser.TryParse(json, out var manifest, out var error).Should().BeTrue(error);

        manifest!.Version.Should().Be("202609");
        manifest.File.Should().Be("aifa-202609.zip");
        manifest.Generated.Should().Be(new DateTimeOffset(2026, 9, 29, 11, 38, 1, TimeSpan.Zero).AddTicks(1513810));
        manifest.Sha256.Should().BeNull();
        manifest.Size.Should().BeNull();
        manifest.ExpectedFileName.Should().Be("aifa-202609.zip");
    }

    [Fact]
    public void Parses_sha256_and_size_and_lowercases_the_hash()
    {
        var json = $$"""{"version":"202610","sha256":"{{Hash.ToUpperInvariant()}}","size":5016171}""";

        CatalogueFeedManifestParser.TryParse(json, out var manifest, out _).Should().BeTrue();

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
    public void Rejects_invalid_manifests(string json)
    {
        CatalogueFeedManifestParser.TryParse(json, out var manifest, out var error).Should().BeFalse();

        manifest.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_unparsable_generated_timestamp_is_ignored()
    {
        const string json = """{"version":"202609","generated":"yesterday"}""";

        CatalogueFeedManifestParser.TryParse(json, out var manifest, out _).Should().BeTrue();

        manifest!.Generated.Should().BeNull();
    }
}
