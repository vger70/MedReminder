using System.Data;
using System.Text.RegularExpressions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Backup;

// DB-file backup / restore.
//
// Export: uses the SQLite online-backup API
// (SqliteConnection.BackupDatabase), which is thread-safe with
// respect to concurrent writes — SQLite handles page-level locking
// and produces a consistent destination file even while writes from
// other connections are in progress. This is the technique
// recommended by SQLite (https://sqlite.org/backup.html) and
// replaces the previous File.Copy + WAL checkpoint (the checkpoint
// narrowed but did not eliminate the torn-write window).
//
// Multi-profile (Increment 15c, docs/ANALYSIS-MULTI-USER.md §11):
// ExportProfileAsync targets any profile by id, ImportProfileAsync
// replaces any profile's DB. The file name embeds the profileId so
// backups of different profiles can coexist in the same directory;
// PruneOldBackupsAsync applies retention per profileId so the most
// recent backup of profile A does not shield old backups of profile
// B.
internal sealed class BackupService : IBackupService
{
    // File naming: "medreminder-<profileId>-YYYYMMDD-HHmmss.db".
    // profileId is a Guid "N" (32 lowercase hex chars) OR the literal
    // "default" from the V1→V2 migration. The regex captures the id
    // so retention can group files by profile.
    private static readonly Regex BackupFileRegex = new(
        @"^medreminder-(?<profileId>[0-9a-fA-F]{32}|default)-(?<timestamp>\d{8}-\d{6})\.db$",
        RegexOptions.Compiled);

    // C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.3): sibling
    // regex for the encrypted .mrz cloud snapshots. Distinct from
    // BackupFileRegex so pruning of one target never touches the other,
    // even when the user points both targets at the same folder.
    private static readonly Regex CloudBackupFileRegex = new(
        @"^medreminder-(?<profileId>[0-9a-fA-F]{32}|default)-(?<timestamp>\d{8}-\d{6})\.mrz$",
        RegexOptions.Compiled);

    internal static Regex CloudBackupFileRegexForTests => CloudBackupFileRegex;
    internal static Regex BackupFileRegexForTests => BackupFileRegex;

    private readonly MedReminderDbContext _db;
    private readonly TimeProvider _clock;
    private readonly DatabasePathProvider _databasePathProvider;

    public BackupService(
        MedReminderDbContext db,
        TimeProvider clock,
        DatabasePathProvider databasePathProvider)
    {
        _db = db;
        _clock = clock;
        _databasePathProvider = databasePathProvider;
    }

    public string DatabasePath => _databasePathProvider.DatabasePath;

    public async Task<string> ExportProfileAsync(
        string profileId,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);

        var sourcePath = ResolveProfileDatabasePath(profileId);
        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException(
                $"Database file '{sourcePath}' does not exist for profile '{profileId}'.");
        }

        var timestamp = _clock.GetUtcNow().ToString("yyyyMMdd-HHmmss");
        var destinationFile = Path.Combine(
            destinationDirectory,
            $"medreminder-{profileId}-{timestamp}.db");

        var sourceConnectionString =
            new SqliteConnectionStringBuilder(
                AppDataPaths.BuildSqliteConnectionString(sourcePath))
            {
                Cache = SqliteCacheMode.Private,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

        var destConnectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = destinationFile,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

        await using var source = new SqliteConnection(sourceConnectionString);
        await using var dest = new SqliteConnection(destConnectionString);
        await source.OpenAsync(cancellationToken);
        await dest.OpenAsync(cancellationToken);

        // BackupDatabase is synchronous — run it on the ThreadPool so
        // a UI thread caller does not block.
        await Task.Run(() => source.BackupDatabase(dest), cancellationToken);

        _ = _db;
        return destinationFile;
    }

    public Task<int> PruneOldBackupsAsync(
        string directory, int retentionDays, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (retentionDays <= 0) return Task.FromResult(0);
        if (!Directory.Exists(directory)) return Task.FromResult(0);

        var cutoff = _clock.GetUtcNow().UtcDateTime.AddDays(-retentionDays);

        // Group by profileId so retention is applied per profile: the
        // most recent backup of profile A does not shield old backups
        // of profile B (§11.1). Files that do not match the expected
        // pattern (user-renamed backups, other unrelated files) are
        // left alone.
        var candidates = new List<(string Path, string ProfileId, DateTime LastWriteUtc)>();
        foreach (var path in Directory.EnumerateFiles(directory, "medreminder-*.db"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            var match = BackupFileRegex.Match(name);
            if (!match.Success) continue;
            try
            {
                candidates.Add((path, match.Groups["profileId"].Value.ToLowerInvariant(),
                    File.GetLastWriteTimeUtc(path)));
            }
            catch
            {
                // Locked / permission problem: skip.
            }
        }

        var deleted = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.LastWriteUtc < cutoff)
            {
                try
                {
                    File.Delete(candidate.Path);
                    deleted++;
                }
                catch
                {
                    // A locked file must not stop pruning the others.
                }
            }
        }

        return Task.FromResult(deleted);
    }

    public async Task<int> PruneCloudFolderAsync(
        IArchiveStorage storage, int retentionDays, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (retentionDays <= 0) return 0;

        var cutoff = _clock.GetUtcNow().AddDays(-retentionDays);
        var archives = await storage.ListAsync(cancellationToken);

        var deleted = 0;
        foreach (var archive in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Only the C.3+ .mrz naming (§3.3) is eligible: a user's
            // hand-copied archive with a different name is left alone.
            if (!CloudBackupFileRegex.IsMatch(archive.Name)) continue;
            if (archive.CreatedAtUtc >= cutoff) continue;

            try
            {
                await storage.DeleteAsync(archive.Id, cancellationToken);
                deleted++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A locked file must not stop pruning the others.
            }
        }

        return deleted;
    }

    public Task ImportProfileAsync(
        string profileId, string sourceFilePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Backup file not found.", sourceFilePath);
        }

        var target = ResolveProfileDatabasePath(profileId);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        SqliteConnection.ClearAllPools();

        // Also close the DbContext connection when the target belongs
        // to the currently-active profile (i.e. the DbContext's own
        // path); a background-profile import does not need it.
        if (string.Equals(target, DatabasePath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var conn = _db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Closed)
                {
                    conn.Close();
                }
            }
            catch
            {
                // ClearAllPools has already done the heavy lifting.
            }
        }

        if (File.Exists(target))
        {
            var backupName = $"{target}.bak-{_clock.GetUtcNow():yyyyMMddHHmmss}";
            File.Move(target, backupName, overwrite: false);
        }
        File.Copy(sourceFilePath, target, overwrite: false);

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var side = target + suffix;
            if (File.Exists(side))
            {
                try { File.Delete(side); } catch { /* ignore */ }
            }
        }

        _ = cancellationToken;
        return Task.CompletedTask;
    }

    private static string ResolveProfileDatabasePath(string profileId) =>
        Path.Combine(
            AppDataPaths.GetProfileDataDirectory(profileId),
            AppDataPaths.DatabaseFileName);
}
