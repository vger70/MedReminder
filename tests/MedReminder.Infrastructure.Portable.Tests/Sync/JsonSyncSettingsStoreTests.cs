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
}
