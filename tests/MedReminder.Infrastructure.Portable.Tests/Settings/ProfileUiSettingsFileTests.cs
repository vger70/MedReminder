using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.GuidedSetup;
using MedReminder.Domain.Notifications;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Main_window_placement_round_trips_through_the_profile_file(bool maximized)
    {
        var placement = new MainWindowPlacement(-8, 40, 1280, 720, maximized);

        ProfileUiSettingsFile.WriteMainWindow(_dir, placement);

        ProfileUiSettingsFile.ReadMainWindow(_dir).Should().Be(placement);
        File.Exists(FilePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Missing_placement_reads_as_null()
    {
        ProfileUiSettingsFile.WriteTextSize(_dir, TextSize.Large);

        ProfileUiSettingsFile.ReadMainWindow(_dir).Should().BeNull();
    }

    [Fact]
    public void Writing_the_placement_keeps_the_other_preferences_and_unknown_keys()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"TextSize\": \"Large\", \"Appearance\": \"Dark\", \"Future\": 1 }");

        ProfileUiSettingsFile.WriteMainWindow(_dir, new MainWindowPlacement(0, 0, 900, 600, false));
        ProfileUiSettingsFile.Write(_dir, TextSize.ExtraLarge, AppearanceMode.Light);

        ProfileUiSettingsFile.ReadTextSize(_dir).Should().Be(TextSize.ExtraLarge);
        ProfileUiSettingsFile.ReadAppearance(_dir).Should().Be(AppearanceMode.Light);
        ProfileUiSettingsFile.ReadMainWindow(_dir).Should().Be(new MainWindowPlacement(0, 0, 900, 600, false));
        File.ReadAllText(FilePath).Should().Contain("\"Future\": 1");
    }

    [Theory]
    [InlineData("{ \"MainWindow\": { \"X\": 0, \"Y\": 0, \"Width\": 900 } }")]
    [InlineData("{ \"MainWindow\": { \"X\": \"a\", \"Y\": 0, \"Width\": 900, \"Height\": 600 } }")]
    [InlineData("{ \"MainWindow\": { \"X\": 0, \"Y\": 0, \"Width\": 0, \"Height\": 600 } }")]
    [InlineData("{ \"MainWindow\": [] }")]
    [InlineData("{ \"TextSize\": \"Large\", \"textsize\": \"Normal\" }")]
    [InlineData("not json")]
    public void Damaged_placement_reads_as_null(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);

        ProfileUiSettingsFile.ReadMainWindow(_dir).Should().BeNull();
    }

    [Fact]
    public void Navigation_width_round_trips_and_keeps_the_placement()
    {
        var placement = new MainWindowPlacement(0, 0, 900, 600, false);
        ProfileUiSettingsFile.WriteMainWindow(_dir, placement);

        ProfileUiSettingsFile.WriteNavigationWidth(_dir, 320);

        ProfileUiSettingsFile.ReadNavigationWidth(_dir).Should().Be(320);
        ProfileUiSettingsFile.ReadMainWindow(_dir).Should().Be(placement);
    }

    [Theory]
    [InlineData("{ }")]
    [InlineData("{ \"NavigationWidth\": 0 }")]
    [InlineData("{ \"NavigationWidth\": \"wide\" }")]
    [InlineData("{ \"NavigationWidth\": {} }")]
    [InlineData("not json")]
    public void Missing_or_damaged_navigation_width_reads_as_null(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);

        ProfileUiSettingsFile.ReadNavigationWidth(_dir).Should().BeNull();
    }

    [Fact]
    public void Writing_over_a_damaged_file_replaces_it()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");

        ProfileUiSettingsFile.WriteAppearance(_dir, AppearanceMode.Dark);

        ProfileUiSettingsFile.ReadAppearance(_dir).Should().Be(AppearanceMode.Dark);
    }

    [Fact]
    public void Normal_keeps_the_default_look()
    {
        TextSizes.ScaleOf(TextSize.Normal).Should().Be(1f);
        TextSizes.ScaleOf(TextSize.Large).Should().BeGreaterThan(1f);
        TextSizes.ScaleOf(TextSize.ExtraLarge).Should().BeGreaterThan(TextSizes.ScaleOf(TextSize.Large));
    }

    // Guided setup (docs/prompt/PROMPT-GUIDED-SETUP.md): device-local values.
    [Fact]
    public void Guided_setup_is_not_shown_until_written()
    {
        ProfileUiSettingsFile.ReadGuidedSetupShown(_dir).Should().BeFalse();

        ProfileUiSettingsFile.WriteGuidedSetupShown(_dir);

        ProfileUiSettingsFile.ReadGuidedSetupShown(_dir).Should().BeTrue();
    }

    [Theory]
    [InlineData("{ \"GuidedSetupShown\": \"yes\" }")]
    [InlineData("{ \"GuidedSetupShown\": 1 }")]
    [InlineData("{ \"GuidedSetupShown\": false }")]
    [InlineData("not json")]
    public void Damaged_guided_setup_flag_reads_as_not_shown(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);

        ProfileUiSettingsFile.ReadGuidedSetupShown(_dir).Should().BeFalse();
    }

    [Theory]
    [InlineData(7, NotificationChannels.Windows)]
    [InlineData(14, NotificationChannels.Email)]
    [InlineData(0, NotificationChannels.Both)]
    [InlineData(365, NotificationChannels.Both)]
    public void New_medicine_defaults_round_trip(int days, NotificationChannels channels)
    {
        ProfileUiSettingsFile.WriteNewMedicineDefaults(_dir, new NewMedicineDefaults(days, channels));

        ProfileUiSettingsFile.ReadNewMedicineDefaults(_dir).Should().Be(new NewMedicineDefaults(days, channels));
        File.ReadAllText(FilePath).Should().Contain($"\"NewMedicineChannels\": \"{channels}\"");
    }

    [Fact]
    public void Guided_setup_values_keep_the_other_preferences()
    {
        ProfileUiSettingsFile.Write(_dir, TextSize.Large, AppearanceMode.Dark);

        ProfileUiSettingsFile.WriteNewMedicineDefaults(_dir, new NewMedicineDefaults(10, NotificationChannels.Both));
        ProfileUiSettingsFile.WriteGuidedSetupShown(_dir);

        ProfileUiSettingsFile.ReadTextSize(_dir).Should().Be(TextSize.Large);
        ProfileUiSettingsFile.ReadAppearance(_dir).Should().Be(AppearanceMode.Dark);
        ProfileUiSettingsFile.ReadNewMedicineDefaults(_dir).Should().Be(new NewMedicineDefaults(10, NotificationChannels.Both));
    }

    [Theory]
    [InlineData("{ }")]
    [InlineData("{ \"NewMedicineThresholdDays\": 10 }")]
    [InlineData("{ \"NewMedicineChannels\": \"Windows\" }")]
    [InlineData("{ \"NewMedicineThresholdDays\": 400, \"NewMedicineChannels\": \"Windows\" }")]
    [InlineData("{ \"NewMedicineThresholdDays\": -1, \"NewMedicineChannels\": \"Windows\" }")]
    [InlineData("{ \"NewMedicineThresholdDays\": \"10\", \"NewMedicineChannels\": \"Windows\" }")]
    [InlineData("{ \"NewMedicineThresholdDays\": 10, \"NewMedicineChannels\": \"None\" }")]
    [InlineData("{ \"NewMedicineThresholdDays\": 10, \"NewMedicineChannels\": \"Pager\" }")]
    [InlineData("{ \"NewMedicineThresholdDays\": 10, \"NewMedicineChannels\": \"2\" }")]
    [InlineData("{ \"NewMedicineThresholdDays\": 10, \"NewMedicineChannels\": 2 }")]
    [InlineData("not json")]
    public void Missing_or_damaged_new_medicine_defaults_read_as_null(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);

        ProfileUiSettingsFile.ReadNewMedicineDefaults(_dir).Should().BeNull();
    }

    [Fact]
    public void Invalid_new_medicine_defaults_are_not_written()
    {
        var act = () => ProfileUiSettingsFile.WriteNewMedicineDefaults(_dir,
            new NewMedicineDefaults(7, NotificationChannels.None));

        act.Should().Throw<ArgumentException>();
        File.Exists(FilePath).Should().BeFalse();
    }
}
