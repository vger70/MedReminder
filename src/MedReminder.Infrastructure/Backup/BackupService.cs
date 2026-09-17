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
// Retention: pattern medreminder-*.db, nothing else (the user might
// have placed other files in the same folder).
//
// Import: before replacing the current file, forces release of the
// still-pooled SQLite handles (ClearAllPools). The caller (UI) must
// restart the app right after — IApplicationRestarter exists for
// that.
internal sealed class BackupService : IBackupService
{
    private static readonly Regex BackupFileRegex =
        new(@"^medreminder-\d{8}-\d{6}\.db$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly MedReminderDbContext _db;
    private readonly TimeProvider _clock;

    public BackupService(MedReminderDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public string DatabasePath => AppDataPaths.GetDatabasePath();

    public async Task<string> ExportAsync(
        string destinationDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);

        var sourcePath = DatabasePath;
        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException(
                $"Database file '{sourcePath}' does not exist.");
        }

        var timestamp = _clock.GetUtcNow().ToString("yyyyMMdd-HHmmss");
        var destinationFile = Path.Combine(
            destinationDirectory, $"medreminder-{timestamp}.db");

        // Use a dedicated connection on the main DB (read-write, but
        // only for the backup) instead of the DbContext's scoped one:
        // avoids leaving state in the shared connection pool and
        // makes the call safe even when invoked outside the DI
        // request cycle. Cache=Private so we do not share the app's
        // shared cache.
        var sourceConnectionString =
            new SqliteConnectionStringBuilder(AppDataPaths.BuildSqliteConnectionString())
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

        // BackupDatabase is synchronous (Microsoft.Data.Sqlite has
        // no async overload): call it on the ThreadPool so we do
        // not block any UI thread caller.
        await Task.Run(() => source.BackupDatabase(dest), cancellationToken);

        _ = _db; // the field is kept injected for consistency with the scoped lifecycle
        return destinationFile;
    }

    public Task<int> PruneOldBackupsAsync(
        string directory, int retentionDays, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (retentionDays <= 0) return Task.FromResult(0);
        if (!Directory.Exists(directory)) return Task.FromResult(0);

        var cutoff = _clock.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var deleted = 0;

        foreach (var path in Directory.EnumerateFiles(directory, "medreminder-*.db"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Double filter: both the glob and the exact regex.
            // Rejects "medreminder-manual-x.db" or user-renamed
            // backups: it is not our job to delete them.
            var name = Path.GetFileName(path);
            if (!BackupFileRegex.IsMatch(name)) continue;

            try
            {
                var lastWriteUtc = File.GetLastWriteTimeUtc(path);
                if (lastWriteUtc < cutoff)
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch
            {
                // A locked file or a permission issue must not stop
                // the pruning of the others. The failure is only
                // visible as "file left in the folder".
            }
        }

        return Task.FromResult(deleted);
    }

    public Task ImportAsync(string sourceFilePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Backup file not found.", sourceFilePath);
        }

        var target = DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Close any pooled connection with path == target, otherwise
        // File.Move fails with "Sharing violation" on Windows.
        // ClearAllPools() is idempotent and does not throw if there
        // are no pools.
        SqliteConnection.ClearAllPools();

        // Also close the underlying connection of the scoped
        // DbContext, if the caller has not already done so
        // (paranoia): the DbContext is scoped but
        // Microsoft.Data.Sqlite's pooling works underneath.
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
            // Do not block the import over a defensive close
            // failure: ClearAllPools has already done the heavy
            // lifting.
        }

        if (File.Exists(target))
        {
            var backupName = $"{target}.bak-{_clock.GetUtcNow():yyyyMMddHHmmss}";
            File.Move(target, backupName, overwrite: false);
        }
        File.Copy(sourceFilePath, target, overwrite: false);

        // Remove any residual WAL / SHM files of the old DB: the
        // new base is "clean" and will be re-initialized by
        // DatabaseInitializer on the next startup.
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
}
