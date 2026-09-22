namespace MedReminder.Infrastructure.Storage;

// Standard application paths under %LOCALAPPDATA%\MedReminder\
// (docs/ANALYSIS.md §1.1 item 13). Not inside the install directory:
// respects user permissions and survives updates.
//
// Increment 15a (docs/ANALYSIS-MULTI-USER.md §2.5) reshapes this
// class: the previous single-user GetDatabasePath() has been removed
// — the database now lives under a per-profile folder, and its path
// is served by ICurrentProfile.DatabasePath. Global paths
// (smtp.settings.json, smtp.protected, backup.settings.json,
// backup.state.json, profiles.json, logs\) stay here as static
// getters: they are admin-managed and unique per machine + Windows
// user (§2.4).
public static class AppDataPaths
{
    public const string ApplicationFolderName = "MedReminder";
    public const string DatabaseFileName = "medreminder.db";
    public const string CredentialsFileName = "smtp.protected";
    public const string LogsFolderName = "logs";
    public const string ProfilesFolderName = "profiles";
    public const string ProfilesRegistryFileName = "profiles.json";
    public const string DonationsSettingsFileName = "donations.settings.json";

    public static string GetAppDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        var appDirectory = Path.Combine(localAppData, ApplicationFolderName);
        Directory.CreateDirectory(appDirectory);
        return appDirectory;
    }

    public static string GetCredentialsPath() =>
        Path.Combine(GetAppDataDirectory(), CredentialsFileName);

    public static string GetDonationsSettingsPath() =>
        Path.Combine(GetAppDataDirectory(), DonationsSettingsFileName);

    public static string GetLogsDirectory()
    {
        var directory = Path.Combine(GetAppDataDirectory(), LogsFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    // Path of the multi-user registry file (§2.2). The file itself is
    // created / updated by ProfileRegistry — this getter never
    // materializes it.
    public static string GetProfilesRegistryPath() =>
        Path.Combine(GetAppDataDirectory(), ProfilesRegistryFileName);

    // Root directory that contains one folder per profile
    // (§3 on-disk layout). Created on demand.
    public static string GetProfilesRootDirectory()
    {
        var directory = Path.Combine(GetAppDataDirectory(), ProfilesFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    // Data directory of the profile with the given id (§3).
    // Created on demand. The caller is responsible for validating the
    // id (a Guid "N" string or the literal "default" from the V1→V2
    // migration).
    public static string GetProfileDataDirectory(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var directory = Path.Combine(GetProfilesRootDirectory(), profileId);
        Directory.CreateDirectory(directory);
        return directory;
    }

    // Builds the connection string for a specific database file.
    // The path is required — Increment 15a removed the implicit
    // per-machine default so every caller states which DB it is
    // opening.
    public static string BuildSqliteConnectionString(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return $"Data Source={databasePath};Cache=Shared;Foreign Keys=True";
    }
}
