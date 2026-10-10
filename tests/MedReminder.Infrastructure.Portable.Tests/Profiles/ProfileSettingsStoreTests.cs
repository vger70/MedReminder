using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Profiles;
using MedReminder.Infrastructure.Sync;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Profiles;

// Android plan M0, backlog B0-01: the profile settings store, the sync
// status of a profile and the current profile run from a host-supplied
// root, without AppDataPaths.
public sealed class ProfileSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-profile-settings-" + Guid.NewGuid().ToString("N"));
    private readonly string _profilesRoot;
    private readonly ProfileRegistry _registry;

    public ProfileSettingsStoreTests()
    {
        _profilesRoot = Path.Combine(_root, "profiles");
        _registry = new ProfileRegistry(Path.Combine(_root, "profiles.json"), _profilesRoot, TimeProvider.System);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void CurrentProfile_places_the_profile_files_under_the_given_root()
    {
        var profile = _registry.Create("Anna", ProfileRole.Admin);

        var current = new CurrentProfile(profile, _profilesRoot);

        current.DataDirectory.Should().Be(Path.Combine(_profilesRoot, profile.Id));
        Directory.Exists(current.DataDirectory).Should().BeTrue();
        current.DatabasePath.Should().Be(Path.Combine(current.DataDirectory, "medreminder.db"));
        current.NotificationSettingsPath.Should().Be(Path.Combine(current.DataDirectory, "notifications.settings.json"));
        current.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public void Write_renames_the_profile_and_writes_the_notifications_section()
    {
        var profile = _registry.Create("Anna", ProfileRole.Admin);
        var current = new CurrentProfile(profile, _profilesRoot);
        var store = new ProfileSettingsStore(current, _registry);

        store.Write(new Dictionary<string, string?>
        {
            [ProfileSetting.DisplayName] = "Anna Rossi",
            [ProfileSetting.ToAddress] = "anna@example.org",
            [ProfileSetting.PackageExpiryLeadDays] = "30",
        });

        _registry.GetById(profile.Id)!.DisplayName.Should().Be("Anna Rossi");
        using var document = JsonDocument.Parse(File.ReadAllText(current.NotificationSettingsPath));
        var section = document.RootElement.GetProperty(NotificationSettings.SectionName);
        section.GetProperty(nameof(NotificationSettings.ToAddress)).GetString().Should().Be("anna@example.org");
        section.GetProperty(nameof(NotificationSettings.PackageExpiryLeadDays)).GetString().Should().Be("30");

        var read = store.Read();
        read[ProfileSetting.DisplayName].Should().Be("Anna Rossi");
        read[ProfileSetting.ToAddress].Should().Be("anna@example.org");
        read[ProfileSetting.CaregiverAddress].Should().BeEmpty();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Read_treats_a_damaged_notifications_file_as_empty(string content)
    {
        var profile = _registry.Create("Anna", ProfileRole.Admin);
        var current = new CurrentProfile(profile, _profilesRoot);
        File.WriteAllText(current.NotificationSettingsPath, content);
        var store = new ProfileSettingsStore(current, _registry);

        var settings = store.Read();

        settings[ProfileSetting.DisplayName].Should().Be("Anna");
        settings[ProfileSetting.ToAddress].Should().BeEmpty();
    }

    [Fact]
    public void Read_treats_an_unreadable_notifications_file_as_empty()
    {
        var profile = _registry.Create("Anna", ProfileRole.Admin);
        var current = new CurrentProfile(profile, _profilesRoot);
        File.WriteAllText(current.NotificationSettingsPath, """{ "Notifications": { "ToAddress": "anna@example.org" } }""");
        var store = new ProfileSettingsStore(current, _registry);
        // An exclusive handle makes the read fail with an IOException.
        using var locked = new FileStream(current.NotificationSettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        store.Read()[ProfileSetting.ToAddress].Should().BeEmpty();
    }

    [Fact]
    public void Write_replaces_a_damaged_notifications_file()
    {
        var profile = _registry.Create("Anna", ProfileRole.Admin);
        var current = new CurrentProfile(profile, _profilesRoot);
        File.WriteAllText(current.NotificationSettingsPath, "{ not json");
        var store = new ProfileSettingsStore(current, _registry);

        store.Write(new Dictionary<string, string?> { [ProfileSetting.ToAddress] = "anna@example.org" });

        store.Read()[ProfileSetting.ToAddress].Should().Be("anna@example.org");
    }

    [Fact]
    public void Write_rejects_an_unknown_setting()
    {
        var profile = _registry.Create("Anna", ProfileRole.Admin);
        var store = new ProfileSettingsStore(new CurrentProfile(profile, _profilesRoot), _registry);

        FluentActions.Invoking(() => store.Write(new Dictionary<string, string?> { ["Unknown"] = "x" }))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SyncProfileStatus_follows_the_sync_settings_file_of_the_profile()
    {
        var profile = _registry.Create("Anna", ProfileRole.Admin);
        var current = new CurrentProfile(profile, _profilesRoot);
        var status = new SyncProfileStatus(_profilesRoot);

        status.IsSyncEnabled(profile.Id).Should().BeFalse();

        File.WriteAllText(Path.Combine(current.DataDirectory, JsonSyncSettingsStore.FileName), "{}");

        status.IsSyncEnabled(profile.Id).Should().BeTrue();
        status.IsSyncEnabled("other").Should().BeFalse();
    }
}
