using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.Infrastructure.Profiles;

// Concrete ICurrentProfile assembled once, in the composition root,
// after the boot flow (picker / first-run wizard / hint) has decided
// which profile to open (docs/ANALYSIS-MULTI-USER.md §4.1).
//
// In Increment 15a nobody registers it in the DI container: the
// class is available and unit-testable, but the app still boots as
// single-user. Wiring happens in 15c.
public sealed class CurrentProfile : ICurrentProfile
{
    public CurrentProfile(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Id = profile.Id;
        DisplayName = profile.DisplayName;
        Role = profile.Role;
        DataDirectory = AppDataPaths.GetProfileDataDirectory(profile.Id);
        DatabasePath = Path.Combine(DataDirectory, AppDataPaths.DatabaseFileName);
        NotificationSettingsPath = Path.Combine(DataDirectory, "notifications.settings.json");
    }

    public string Id { get; }
    public string DisplayName { get; }
    public ProfileRole Role { get; }
    public bool IsAdmin => Role == ProfileRole.Admin;
    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string NotificationSettingsPath { get; }
}
