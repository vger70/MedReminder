using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Settings;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Settings;

public sealed class ProfileUiSettingsFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mr-ui-" + Guid.NewGuid().ToString("N"));
    private string FilePath => ProfileUiSettingsFile.PathFor(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData(TextSize.Normal)]
    [InlineData(TextSize.Large)]
    [InlineData(TextSize.ExtraLarge)]
    public void Text_size_round_trips_through_the_profile_file(TextSize size)
    {
        ProfileUiSettingsFile.WriteTextSize(_dir, size);

        ProfileUiSettingsFile.ReadTextSize(_dir).Should().Be(size);
        File.ReadAllText(FilePath).Should().Contain($"\"TextSize\": \"{size}\"");
        File.Exists(FilePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Missing_directory_reads_as_normal()
        => ProfileUiSettingsFile.ReadTextSize(_dir).Should().Be(TextSize.Normal);

    [Theory]
    [InlineData("{ \"TextSize\": \"Huge\" }")]
    [InlineData("{ \"TextSize\": \"2\" }")]
    [InlineData("{ \"TextSize\": 2 }")]
    [InlineData("{ }")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Unknown_or_missing_value_reads_as_normal(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);

        ProfileUiSettingsFile.ReadTextSize(_dir).Should().Be(TextSize.Normal);
    }

    [Fact]
    public void Property_name_and_value_are_case_insensitive()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"textsize\": \"extralarge\" }");

        ProfileUiSettingsFile.ReadTextSize(_dir).Should().Be(TextSize.ExtraLarge);
    }

    [Theory]
    [InlineData(AppearanceMode.System)]
    [InlineData(AppearanceMode.Light)]
    [InlineData(AppearanceMode.Dark)]
    public void Appearance_round_trips_through_the_profile_file(AppearanceMode mode)
    {
        ProfileUiSettingsFile.WriteAppearance(_dir, mode);

        ProfileUiSettingsFile.ReadAppearance(_dir).Should().Be(mode);
        File.ReadAllText(FilePath).Should().Contain($"\"Appearance\": \"{mode}\"");
        File.Exists(FilePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Writing_one_preference_keeps_the_other()
    {
        ProfileUiSettingsFile.WriteTextSize(_dir, TextSize.Large);
        ProfileUiSettingsFile.WriteAppearance(_dir, AppearanceMode.Dark);
        ProfileUiSettingsFile.WriteTextSize(_dir, TextSize.ExtraLarge);

        ProfileUiSettingsFile.ReadTextSize(_dir).Should().Be(TextSize.ExtraLarge);
        ProfileUiSettingsFile.ReadAppearance(_dir).Should().Be(AppearanceMode.Dark);
    }

    [Fact]
    public void File_written_before_the_appearance_setting_reads_as_system()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"TextSize\": \"Large\" }");

        ProfileUiSettingsFile.ReadAppearance(_dir).Should().Be(AppearanceMode.System);
        ProfileUiSettingsFile.ReadTextSize(_dir).Should().Be(TextSize.Large);
    }

    [Theory]
    [InlineData("{ \"Appearance\": \"Sepia\" }")]
    [InlineData("{ \"Appearance\": \"2\" }")]
    [InlineData("{ \"Appearance\": 2 }")]
    [InlineData("not json")]
    public void Unknown_appearance_reads_as_system(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);

        ProfileUiSettingsFile.ReadAppearance(_dir).Should().Be(AppearanceMode.System);
    }

    [Fact]
    public void Normal_keeps_the_default_look()
    {
        TextSizes.ScaleOf(TextSize.Normal).Should().Be(1f);
        TextSizes.ScaleOf(TextSize.Large).Should().BeGreaterThan(1f);
        TextSizes.ScaleOf(TextSize.ExtraLarge).Should().BeGreaterThan(TextSizes.ScaleOf(TextSize.Large));
    }
}
