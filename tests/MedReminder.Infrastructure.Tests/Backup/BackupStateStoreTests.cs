using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Backup;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Backup;

// backup.state.json: a file written before the per-target days fills them
// from LastSuccessfulBackupAt, so the upgrade does not back up a day that
// is already covered; a newer file keeps them null, so a manual backup
// does not close the automatic day. Update reads, changes and saves in one
// step and writes nothing when the change returns the same state.
public sealed class BackupStateStoreTests : IDisposable
{
    private readonly string _folder;
    private readonly string _path;

    public BackupStateStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "mr-backup-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "backup.state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void File_without_the_target_days_takes_the_last_successful_backup_for_both()
    {
        File.WriteAllText(_path, """
            {
              "LastSuccessfulBackupAt": "2026-10-20T03:00:00+00:00",
              "LastAttemptAt": "2026-10-20T03:00:00+00:00",
              "LastError": null,
              "LastBackupFile": "backup.db"
            }
            """);

        var state = new BackupStateStore(_path).Load();

        var expected = new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);
        state.LastLocalBackupAt.Should().Be(expected);
        state.LastCloudBackupAt.Should().Be(expected);
    }

    [Fact]
    public void File_with_null_target_days_keeps_them_null()
    {
        var store = new BackupStateStore(_path);
        store.Update(s => s with { LastSuccessfulBackupAt = DateTimeOffset.UtcNow, LastBackupFile = "manual.db" });

        var state = new BackupStateStore(_path).Load();

        state.LastSuccessfulBackupAt.Should().NotBeNull();
        state.LastLocalBackupAt.Should().BeNull();
        state.LastCloudBackupAt.Should().BeNull();
    }

    [Fact]
    public void Update_applies_the_change_to_the_saved_state()
    {
        var store = new BackupStateStore(_path);
        store.Update(s => s with { LastBackupFile = "a.db", SignInRequired = CloudProvider.OneDrive });

        var result = store.Update(s => s with { LastError = "boom" });

        result.LastBackupFile.Should().Be("a.db");
        result.SignInRequired.Should().Be(CloudProvider.OneDrive);
        new BackupStateStore(_path).Load().Should().Be(result);
    }

    [Fact]
    public void Update_returning_the_same_state_writes_nothing()
    {
        new BackupStateStore(_path).Update(s => s);

        File.Exists(_path).Should().BeFalse();
    }
}
