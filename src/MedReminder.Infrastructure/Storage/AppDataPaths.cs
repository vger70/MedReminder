namespace MedReminder.Infrastructure.Storage;

// Standard application paths under %LOCALAPPDATA%\MedReminder\
// (docs/ANALYSIS.md §1.1 item 13). Not inside the install directory:
// respects user permissions and survives updates.
public static class AppDataPaths
{
    public const string ApplicationFolderName = "MedReminder";
    public const string DatabaseFileName = "medreminder.db";
    public const string CredentialsFileName = "smtp.protected";
    public const string LogsFolderName = "logs";

    public static string GetAppDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        var appDirectory = Path.Combine(localAppData, ApplicationFolderName);
        Directory.CreateDirectory(appDirectory);
        return appDirectory;
    }

    public static string GetDatabasePath() =>
        Path.Combine(GetAppDataDirectory(), DatabaseFileName);

    public static string GetCredentialsPath() =>
        Path.Combine(GetAppDataDirectory(), CredentialsFileName);

    public static string GetLogsDirectory()
    {
        var directory = Path.Combine(GetAppDataDirectory(), LogsFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string BuildSqliteConnectionString(string? overrideDatabasePath = null)
    {
        var path = overrideDatabasePath ?? GetDatabasePath();
        return $"Data Source={path};Cache=Shared;Foreign Keys=True";
    }
}
