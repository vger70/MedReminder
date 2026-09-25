using System.Runtime.Versioning;
using FluentAssertions;
using MedReminder.Infrastructure.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Backup;

// C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.3, §4.5):
// PruneCloudFolderAsync must honour retentionDays, leave archives that
// do not follow the C.3+ naming alone, and never touch the local raw-DB
// .db files — even when the user configures the same folder for both
// targets. Since C.3++ it reaches the folder only through
// IArchiveStorage (docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md §7.5).
// The .mrz regex and the .db regex are asserted independent.
[SupportedOSPlatform("windows")]
public sealed class BackupServicePruneCloudTests : IDisposable
{
    private readonly string _folder;

    public BackupServicePruneCloudTests()
    {
        _folder = Path.Combine(
            Path.GetTempPath(), "mr-cloud-prune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Prune_removes_only_expired_mrz_and_leaves_db_files_alone()
    {
        var recent = "medreminder-" + new string('a', 32) + "-20260901-120000";
        var old = "medreminder-" + new string('a', 32) + "-20250101-090000";
        var recentMrz = WriteFile(recent + ".mrz", ageDays: 1);
        var oldMrz = WriteFile(old + ".mrz", ageDays: 90);
        var oldDb = WriteFile(old + ".db", ageDays: 90);
        var unrelated = WriteFile("unrelated.mrz", ageDays: 400);

        var service = CreateService();

        var deleted = await service.PruneCloudFolderAsync(CreateStorage(_folder), retentionDays: 30, CancellationToken.None);

        deleted.Should().Be(1);
        File.Exists(recentMrz).Should().BeTrue();
        File.Exists(oldMrz).Should().BeFalse();
        File.Exists(oldDb).Should().BeTrue("PruneCloudFolder must not touch .db files");
        File.Exists(unrelated).Should().BeTrue("files that do not match the .mrz regex are left alone");
    }

    [Fact]
    public async Task Retention_of_zero_prunes_nothing()
    {
        var oldMrz = WriteFile(
            "medreminder-" + new string('b', 32) + "-20200101-000000.mrz",
            ageDays: 3650);

        var deleted = await CreateService()
            .PruneCloudFolderAsync(CreateStorage(_folder), retentionDays: 0, CancellationToken.None);

        deleted.Should().Be(0);
        File.Exists(oldMrz).Should().BeTrue();
    }

    [Fact]
    public async Task Missing_folder_prunes_nothing()
    {
        var deleted = await CreateService().PruneCloudFolderAsync(
            CreateStorage(Path.Combine(_folder, "missing")),
            retentionDays: 30,
            CancellationToken.None);

        deleted.Should().Be(0);
    }

    [Fact]
    public void Cloud_regex_matches_mrz_and_rejects_db()
    {
        var mrz = "medreminder-" + new string('c', 32) + "-20260101-120000.mrz";
        var db = "medreminder-" + new string('c', 32) + "-20260101-120000.db";

        BackupService.CloudBackupFileRegexForTests.IsMatch(mrz).Should().BeTrue();
        BackupService.CloudBackupFileRegexForTests.IsMatch(db).Should().BeFalse();
        BackupService.BackupFileRegexForTests.IsMatch(db).Should().BeTrue();
        BackupService.BackupFileRegexForTests.IsMatch(mrz).Should().BeFalse();
    }

    private string WriteFile(string name, int ageDays)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "test");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-ageDays));
        return path;
    }

    private static LocalFolderArchiveStorage CreateStorage(string folder) =>
        new(() => folder, Path.GetTempPath(), NullLogger<LocalFolderArchiveStorage>.Instance);

    // Minimal BackupService for the tests: PruneCloudFolderAsync does
    // not read the DbContext or the profile path, so we hand a
    // throwaway in-memory context + a dummy path provider.
    private static BackupService CreateService()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new MedReminderDbContext(options);
        return new BackupService(
            db,
            TimeProvider.System,
            new DatabasePathProvider(":memory:"));
    }
}
