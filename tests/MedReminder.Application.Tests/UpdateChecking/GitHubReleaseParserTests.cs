using FluentAssertions;
using MedReminder.Application.UpdateChecking;
using Xunit;

namespace MedReminder.Application.Tests.UpdateChecking;

// Exercises the pure JSON parser that turns a GitHub Releases API
// payload into an UpdateCheckResult. All tests are string-based —
// no HTTP is involved.
public class GitHubReleaseParserTests
{
    [Theory]
    [InlineData("v2.0.1", 2, 0, 1)]
    [InlineData("V2.0.1", 2, 0, 1)]
    [InlineData("2.0.1", 2, 0, 1)]
    [InlineData("v10.20.30", 10, 20, 30)]
    [InlineData("v2.0.1-rc1", 2, 0, 1)]
    [InlineData("v2.0", 2, 0, -1)]
    public void TryParseTag_accepts_expected_formats(string tag, int major, int minor, int build)
    {
        GitHubReleaseParser.TryParseTag(tag, out var parsed).Should().BeTrue();
        parsed.Major.Should().Be(major);
        parsed.Minor.Should().Be(minor);
        parsed.Build.Should().Be(build);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("release")]
    [InlineData("v")]
    [InlineData("v.")]
    [InlineData("vX.Y.Z")]
    public void TryParseTag_rejects_garbage(string tag)
    {
        GitHubReleaseParser.TryParseTag(tag, out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_returns_NewVersion_when_tag_is_ahead()
    {
        var json = BuildJson(tag: "v2.1.0", htmlUrl: "https://github.com/vger70/MedReminder/releases/tag/v2.1.0");

        var result = GitHubReleaseParser.Parse(json, new Version(2, 0, 1));

        result.Status.Should().Be(UpdateCheckStatus.NewVersionAvailable);
        result.LatestTag.Should().Be("v2.1.0");
        result.LatestVersion.Should().Be(new Version(2, 1, 0));
        result.ReleaseUrl.Should().Be("https://github.com/vger70/MedReminder/releases/tag/v2.1.0");
    }

    [Fact]
    public void Parse_returns_UpToDate_when_tag_matches_current()
    {
        var json = BuildJson(tag: "v2.0.1");

        var result = GitHubReleaseParser.Parse(json, new Version(2, 0, 1));

        result.Status.Should().Be(UpdateCheckStatus.UpToDate);
    }

    [Fact]
    public void Parse_returns_UpToDate_when_running_ahead_of_remote()
    {
        var json = BuildJson(tag: "v1.5.0");

        var result = GitHubReleaseParser.Parse(json, new Version(2, 0, 1));

        result.Status.Should().Be(UpdateCheckStatus.UpToDate);
    }

    [Fact]
    public void Parse_ignores_assembly_revision_component()
    {
        // Some SDKs stamp a build revision (fourth component) on the
        // AssemblyVersion. It must not defeat the tag comparison.
        var json = BuildJson(tag: "v2.0.1");

        var result = GitHubReleaseParser.Parse(json, new Version(2, 0, 1, 42));

        result.Status.Should().Be(UpdateCheckStatus.UpToDate);
    }

    [Fact]
    public void Parse_treats_prerelease_as_up_to_date()
    {
        var json = BuildJson(tag: "v3.0.0", prerelease: true);

        var result = GitHubReleaseParser.Parse(json, new Version(2, 0, 1));

        result.Status.Should().Be(UpdateCheckStatus.UpToDate);
    }

    [Fact]
    public void Parse_treats_draft_as_up_to_date()
    {
        var json = BuildJson(tag: "v3.0.0", draft: true);

        var result = GitHubReleaseParser.Parse(json, new Version(2, 0, 1));

        result.Status.Should().Be(UpdateCheckStatus.UpToDate);
    }

    [Fact]
    public void Parse_returns_Error_when_tag_is_unparseable()
    {
        var json = BuildJson(tag: "not-a-version");

        var result = GitHubReleaseParser.Parse(json, new Version(2, 0, 1));

        result.Status.Should().Be(UpdateCheckStatus.Error);
        result.ErrorMessage.Should().Contain("not-a-version");
    }

    [Fact]
    public void Parse_returns_Error_when_payload_lacks_tag_name()
    {
        const string json = "{\"html_url\":\"https://example.com\"}";

        var result = GitHubReleaseParser.Parse(json, new Version(2, 0, 1));

        result.Status.Should().Be(UpdateCheckStatus.Error);
        result.ErrorMessage.Should().Contain("tag_name");
    }

    [Fact]
    public void Parse_returns_Error_when_json_is_malformed()
    {
        const string json = "not json";

        var result = GitHubReleaseParser.Parse(json, new Version(2, 0, 1));

        result.Status.Should().Be(UpdateCheckStatus.Error);
    }

    private static string BuildJson(string tag, string? htmlUrl = null, bool prerelease = false, bool draft = false)
    {
        var url = htmlUrl ?? "https://github.com/vger70/MedReminder/releases/latest";
        return "{"
            + $"\"tag_name\":\"{tag}\","
            + $"\"html_url\":\"{url}\","
            + $"\"prerelease\":{(prerelease ? "true" : "false")},"
            + $"\"draft\":{(draft ? "true" : "false")}"
            + "}";
    }
}
