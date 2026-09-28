namespace MedReminder.Application.Abstractions;

// Local copy of the replicated profile settings (B.1, P8 of
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §2, §4.2): the display name in
// the profile registry and the notification recipients in
// notifications.settings.json, both for the current profile. Keys are
// Domain.Sync.ProfileSetting names. Written by the use cases that change
// a setting and by the sync apply step, which projects the winning
// versions into it (ProfileSettingsProjection).
public interface IProfileSettingsStore
{
    // Every ProfileSetting name, an empty address as "".
    IReadOnlyDictionary<string, string?> Read();

    // Only the given settings change.
    void Write(IReadOnlyDictionary<string, string?> changes);
}

// Whether a profile other than the current one takes part in sync
// (its profiles\<id>\sync.settings.json exists). A synced profile is
// renamed only while it is open, so the new name reaches its devices.
public interface ISyncProfileStatus
{
    bool IsSyncEnabled(string profileId);
}
