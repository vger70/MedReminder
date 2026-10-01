using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Infrastructure.Catalogue;

// The shortage list as a JSON file under
// <app data>\catalogue\shortages\shortages-it.json, shared by every
// profile (public reference data; docs/notes/EVOLUTION-PROPOSALS-2.md
// §3.3). Written through a temporary file and a rename, so a reader
// never sees half a file. Parsed once per file version: Load is called
// on every list reload and periodic check. Singleton.
public sealed class JsonFileShortageListStore : IShortageListStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private DateTime _cachedStamp;
    private ShortageList? _cached;

    public JsonFileShortageListStore(IAppDataLocation appData)
        : this(Path.Combine(appData.DataDirectory, "catalogue", "shortages", "shortages-it.json"))
    {
    }

    internal JsonFileShortageListStore(string path)
    {
        _path = path;
    }

    public ShortageList? Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return null;
                var stamp = File.GetLastWriteTimeUtc(_path);
                if (_cached is not null && stamp == _cachedStamp) return _cached;
                var json = File.ReadAllText(_path, Encoding.UTF8);
                _cached = ShortageFeedParser.TryParseList(json, out var list, out _) ? list : null;
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
