namespace MedReminder.Application.Abstractions;

// Sync state of the current profile on this installation
// (profiles\<id>\sync.settings.json, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §7.3). Its presence means sync is enabled:
// only then do use cases record operations (Phase 3a decision, product
// owner 2026-09-27; the genesis snapshot carries everything written
// before). DeviceId identifies this installation in the group and is
// never copied to another one.
//
// Phase 3c: KeyVersion names the group key in use (key.<n>.wrap,
// rotation is Phase 4) and Folder the root of a local-folder transport.
public sealed record SyncSettings(
    Guid GroupId,
    Guid DeviceId,
    int Generation,
    int KeyVersion = 1,
    string? Folder = null);

public interface ISyncSettingsStore
{
    // Null when sync is not enabled for the profile.
    SyncSettings? Load();

    // Null disables sync.
    void Save(SyncSettings? settings);
}
