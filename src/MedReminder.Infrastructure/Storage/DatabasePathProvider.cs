namespace MedReminder.Infrastructure.Storage;

// Singleton holding the SQLite database path decided at composition
// time by the UI. Introduced in Increment 15a
// (docs/ANALYSIS-MULTI-USER.md §2.5) so BackupService and any other
// path-sensitive service can share a single source of truth without
// depending on a still-obsolete AppDataPaths.GetDatabasePath().
//
// In Increment 15c the same slot is filled by
// ICurrentProfile.DatabasePath, or BackupService will accept a
// per-profile path directly (§11.2). Kept internal so it does not
// leak into the Application layer.
internal sealed class DatabasePathProvider
{
    public DatabasePathProvider(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
    }

    public string DatabasePath { get; }
}
