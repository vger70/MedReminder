using System.Text.Json;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Sync;

// profiles\<id>\sync.settings.json (B.1 Phase 3a, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §7.3), next to the profile database. A
// missing file means sync is disabled. An unreadable file throws: if it
// read as "disabled", use cases would silently stop recording operations
// and the devices would diverge. Nothing writes the file before Phase 3d.
internal sealed class JsonSyncSettingsStore : ISyncSettingsStore
{
    public const string FileName = "sync.settings.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();
    private DateTime? _loadedStamp;
    private SyncSettings? _cached;

    public JsonSyncSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public SyncSettings? Load()
    {
        lock (_lock)
        {
            // Read again when the file changed on disk: an import or a
            // restore marks a pending reset through another instance
            // (MarkResetPending), and no sync run may miss it.
            var stamp = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
            if (_loadedStamp != stamp)
            {
                _cached = Read();
                _loadedStamp = stamp;
            }
            return _cached;
        }
    }

    public void Save(SyncSettings? settings)
    {
        lock (_lock)
        {
            if (settings is null)
            {
                File.Delete(_path);
            }
            else
            {
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
                File.Move(tmp, _path, overwrite: true);
            }
            _cached = settings;
            _loadedStamp = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
        }
    }

    // Called before an import or a restore replaces the database of the
    // profile in `profileDirectory` (§5.7): when that profile is synced,
    // its next sync run starts a new generation from the new database
    // instead of applying the current generation to it.
    // Returns how to undo the mark when the swap it announces does not
    // happen; null when there was nothing to mark.
    public static Action? MarkResetPending(string profileDirectory)
    {
        var store = new JsonSyncSettingsStore(Path.Combine(profileDirectory, FileName));
        if (store.Load() is { ResetPending: false } settings)
        {
            store.Save(settings with { ResetPending = true });
            return () => store.Save(settings);
        }
        return null;
    }

    private SyncSettings? Read()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var settings = JsonSerializer.Deserialize<SyncSettings>(File.ReadAllText(_path), Options);
            if (settings is null || settings.GroupId == Guid.Empty || settings.DeviceId == Guid.Empty
                || settings.Generation < 1
                || (settings.Provider is not null && string.IsNullOrEmpty(settings.AccountId)))
            {
                throw new InvalidDataException("Incomplete sync settings.");
            }
            return settings;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Sync settings file {FileName} is not valid JSON.", ex);
        }
    }
}
