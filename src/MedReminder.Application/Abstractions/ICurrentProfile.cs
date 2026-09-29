namespace MedReminder.Application.Abstractions;

// Read-only view of the profile the running process opened at boot
// (docs/ANALYSIS-MULTI-USER.md §2.4). Registered as Singleton in the
// DI container once the picker / first-run wizard has selected one.
// Every service that used to hardcode
// AppDataPaths.GetDatabasePath() takes ICurrentProfile.DatabasePath
// instead.
//
// The active profile cannot change at runtime: switching profile
// goes through IApplicationRestarter and starts a fresh process
// (§6.1).
public interface ICurrentProfile
{
    string Id { get; }
    string DisplayName { get; }
    ProfileRole Role { get; }

    // Convenience for UI gating. Kept as a real property (not a
    // default interface member) so mocking frameworks and tests can
    // set it explicitly.
    bool IsAdmin { get; }

    // %LOCALAPPDATA%\MedReminder\profiles\<Id>\
    string DataDirectory { get; }

    // <DataDirectory>\medreminder.db
    string DatabasePath { get; }

    // <DataDirectory>\notifications.settings.json — the per-profile
    // ToAddress lives here (§7.1). Wired in 15c; the path is exposed
    // now so the abstraction is stable across sub-increments.
    string NotificationSettingsPath { get; }
}
