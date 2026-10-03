using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Infrastructure.Catalogue;

// The equivalents list as a JSON file under
// <app data>\catalogue\equivalents\equivalents-it.json, shared by every
// profile (public reference data;
// docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md §2.4). Written
// through a temporary file and a rename, so a reader never sees half a
// file. Parsed once per file version: Load is called on every list
// reload. Singleton.
public sealed class JsonFileEquivalenceListStore : IEquivalenceListStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private DateTime _cachedStamp;
    private EquivalenceList? _cached;

    public JsonFileEquivalenceListStore(IAppDataLocation appData)
        : this(Path.Combine(appData.DataDirectory, "catalogue", "equivalents", "equivalents-it.json"))
    {
    }

    internal JsonFileEquivalenceListStore(string path)
    {
        _path = path;
    }

    public EquivalenceList? Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return null;
                var stamp = File.GetLastWriteTimeUtc(_path);
                if (_cached is not null && stamp == _cachedStamp) return _cached;
                var json = File.ReadAllText(_path, Encoding.UTF8);
                _cached = EquivalenceFeedParser.TryParseList(json, out var list, out _) ? list : null;
                _cachedStamp = stamp;
                return _cached;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public void Save(byte[] listJson)
    {
        ArgumentNullException.ThrowIfNull(listJson);
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllBytes(temp, listJson);
            File.Move(temp, _path, overwrite: true);
            _cached = null;
        }
    }
}
