namespace MedReminder.Infrastructure.Persistence;

// Connection string for a profile database file. Shared by every host;
// the Windows AppDataPaths.BuildSqliteConnectionString delegates here.
public static class SqliteConnectionStrings
{
    public static string ForFile(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return $"Data Source={databasePath};Cache=Shared;Foreign Keys=True";
    }
}
