using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Sync;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Sync;

public sealed class JsonSyncSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mr-sync-" + Guid.NewGuid().ToString("N"));
    private string PathName => System.IO.Path.Combine(_dir, JsonSyncSettingsStore.FileName);

    public JsonSyncSettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_means_sync_disabled()
        => new JsonSyncSettingsStore(PathName).Load().Should().BeNull();

    [Fact]
    public void Saved_settings_are_read_by_a_new_instance()
    {
        var settings = new SyncSettings(Guid.NewGuid(), Guid.NewGuid(), 2);
        new JsonSyncSettingsStore(PathName).Save(settings);

        new JsonSyncSettingsStore(PathName).Load().Should().Be(settings);
        File.Exists(PathName + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void A_OneDrive_target_is_stored_with_the_provider_name()
    {
        var settings = new SyncSettings(Guid.NewGuid(), Guid.NewGuid(), 1, DeviceName: "PC",
            Provider: CloudProvider.OneDrive, AccountId: "home-account-id");
        new JsonSyncSettingsStore(PathName).Save(settings);

        File.ReadAllText(PathName).Should().Contain("\"Provider\": \"OneDrive\"");
        new JsonSyncSettingsStore(PathName).Load().Should().Be(settings);
    }

    [Fact]
    public void A_Phase_3_file_reads_as_a_folder_target()
    {
        var group = Guid.NewGuid();
        var device = Guid.NewGuid();
        File.WriteAllText(PathName, $$"""
            { "GroupId": "{{group}}", "DeviceId": "{{device}}", "Generation": 1, "KeyVersion": 1,
              "Folder": "C:\\Sync", "DeviceName": "PC", "ResetPending": false }
            """);

        var loaded = new JsonSyncSettingsStore(PathName).Load()!;
        SyncTarget.Of(loaded).Should().Be(SyncTarget.ForFolder("C:\\Sync"));
    }

    [Fact]
    public void A_cloud_target_without_an_account_is_unreadable()
    {
        File.WriteAllText(PathName, $$"""
            { "GroupId": "{{Guid.NewGuid()}}", "DeviceId": "{{Guid.NewGuid()}}", "Generation": 1, "Provider": "OneDrive" }
            """);

        FluentActions.Invoking(() => new JsonSyncSettingsStore(PathName).Load())
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Saving_null_disables_sync()
    {
        var store = new JsonSyncSettingsStore(PathName);
        store.Save(new SyncSettings(Guid.NewGuid(), Guid.NewGuid(), 1));

        store.Save(null);

        store.Load().Should().BeNull();
        File.Exists(PathName).Should().BeFalse();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Unreadable_file_throws_instead_of_disabling_sync(string content)
    {
        File.WriteAllText(PathName, content);

        FluentActions.Invoking(() => new JsonSyncSettingsStore(PathName).Load())
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void A_reset_marked_through_another_instance_is_seen()
    {
        var store = new JsonSyncSettingsStore(PathName);
        store.Save(new SyncSettings(Guid.NewGuid(), Guid.NewGuid(), 1));
        store.Load()!.ResetPending.Should().BeFalse();

        // Keep the timestamps apart on file systems with coarse resolution.
        File.SetLastWriteTimeUtc(PathName, DateTime.UtcNow.AddSeconds(-10));
        store.Load();
        JsonSyncSettingsStore.MarkResetPending(_dir);

        store.Load()!.ResetPending.Should().BeTrue();
    }

    [Fact]
    public void Undoing_a_reset_mark_restores_the_previous_settings()
    {
        var store = new JsonSyncSettingsStore(PathName);
        store.Save(new SyncSettings(Guid.NewGuid(), Guid.NewGuid(), 1));

        var undo = JsonSyncSettingsStore.MarkResetPending(_dir);
        new JsonSyncSettingsStore(PathName).Load()!.ResetPending.Should().BeTrue();
        undo!();

        new JsonSyncSettingsStore(PathName).Load()!.ResetPending.Should().BeFalse();
    }

    [Fact]
    public void Marking_a_profile_without_sync_writes_nothing()
    {
        JsonSyncSettingsStore.MarkResetPending(_dir);

        File.Exists(PathName).Should().BeFalse();
    }
}
