namespace MedReminder.Application.Abstractions;

// User-DB backup (spec §23, Increment 11).
// Export copies the current DB to a user-chosen location using
// SQLite's online-backup API (safe hot backup without stopping the
// app); import restores it after releasing the locks held by pooled
// connections. Retention: removal of files older than N days from the
// same folder.
public interface IBackupService
{
    string DatabasePath { get; }

    // Copies the DB into the given folder (filename derived from a UTC
    // timestamp). Returns the path of the created file.
    Task<string> ExportAsync(string destinationDirectory, CancellationToken cancellationToken);

    // Removes the "medreminder-*.db" files older than retentionDays
    // from the given folder. Returns the number of deleted files.
    // Silent if the folder does not exist or is empty; does NOT touch
    // files that do not match the pattern (the user might have put
    // other things there).
    Task<int> PruneOldBackupsAsync(
        string directory, int retentionDays, CancellationToken cancellationToken);

    // Replaces the current DB with the given one. The caller (UI)
    // must stop the monitor and the scheduler BEFORE; the
    // implementation forces ClearAllPools() on Microsoft.Data.Sqlite
    // to release the handles opened by DbContext pooling.
    Task ImportAsync(string sourceFilePath, CancellationToken cancellationToken);
}
