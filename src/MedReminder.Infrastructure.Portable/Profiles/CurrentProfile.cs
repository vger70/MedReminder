using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Profiles;

// Concrete ICurrentProfile assembled once, in the composition root,
// after the boot flow (picker / first-run wizard / hint) has decided
// which profile to open (docs/ANALYSIS-MULTI-USER.md §4.1).
//
// Portable (Android plan M0, backlog B0-01): the host passes the root
// that holds one folder per profile; on Windows it is
// AppDataPaths.GetProfilesRootDirectory(). The profile folder is
// created on demand, as AppDataPaths.GetProfileDataDirectory does.
public sealed class CurrentProfile : ICurrentProfile
{
    private const string DatabaseFileName = "medreminder.db";
    private const string NotificationSettingsFileName = "notifications.settings.json";

    public CurrentProfile(Profile profile, string profilesRootDirectory)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesRootDirectory);
        Id = profile.Id;
        DisplayName = profile.DisplayName;
        Role = profile.Role;
        DataDirectory = Path.Combine(profilesRootDirectory, profile.Id);
        Directory.CreateDirectory(DataDirectory);
        DatabasePath = Path.Combine(DataDirectory, DatabaseFileName);
        NotificationSettingsPath = Path.Combine(DataDirectory, NotificationSettingsFileName);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public ProfileRole Role { get; }
    public bool IsAdmin => Role == ProfileRole.Admin;
    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string NotificationSettingsPath { get; }
}
