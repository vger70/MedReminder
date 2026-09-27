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
    private bool _loaded;
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
            if (!_loaded)
            {
                _cached = Read();
                _loaded = true;
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
            _loaded = true;
        }
    }

    private SyncSettings? Read()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var settings = JsonSerializer.Deserialize<SyncSettings>(File.ReadAllText(_path), Options);
            if (settings is null || settings.GroupId == Guid.Empty || settings.DeviceId == Guid.Empty
                || settings.Generation < 1)
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
