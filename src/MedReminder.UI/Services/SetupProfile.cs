using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.UI.Services;

// The profile of the host built for the first-run join (household step
// H3d-2), before any real profile exists. Its folder is
// %LOCALAPPDATA%\MedReminder\setup\ and is removed afterwards; its
// database is never opened: the join builds each profile in its own
// staging folder. Administrator: the join window is an administrator's.
internal sealed class SetupProfile : ICurrentProfile
{
    private const string FolderName = "setup";

    private SetupProfile(string directory)
    {
        DataDirectory = directory;
        DatabasePath = Path.Combine(directory, AppDataPaths.DatabaseFileName);
        NotificationSettingsPath = Path.Combine(directory, "notifications.settings.json");
    }

    public string Id => FolderName;
    public string DisplayName => FolderName;
    public ProfileRole Role => ProfileRole.Admin;
    public bool IsAdmin => true;
    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string NotificationSettingsPath { get; }

    public static SetupProfile Create()
    {
        var directory = Path.Combine(AppDataPaths.GetAppDataDirectory(), FolderName);
        Directory.CreateDirectory(directory);
        return new SetupProfile(directory);
    }

    // Best effort: a leftover empty folder holds nothing.
    public static void Remove(SetupProfile profile)
    {
        try
        {
            if (Directory.Exists(profile.DataDirectory)) Directory.Delete(profile.DataDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
