using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Infrastructure.Catalogue;

// A dated list as a JSON file under <app data>\catalogue\<folder>\,
// shared by every profile (public reference data). Written through a
// temporary file and a rename, so a reader never sees half a file.
// Parsed once per file version, a failed parse included: Load is called
// on every list reload. Singleton.
public abstract class JsonFileDatedListStore<TList> : IDatedListStore<TList> where TList : class
{
    private readonly DatedListFeedDefinition<TList> _definition;
    private readonly string _path;
    private readonly object _gate = new();
    private bool _hasCache;
    private DateTime _cachedStamp;
    private TList? _cached;
    private string? _cachedSha256;

    protected JsonFileDatedListStore(DatedListFeedDefinition<TList> definition, string path)
    {
        _definition = definition;
        _path = path;
    }

    // <app data>\catalogue\<folder>\<file name>.
    protected static string PathIn(IAppDataLocation appData, string folder, string fileName)
        => Path.Combine(appData.DataDirectory, "catalogue", folder, fileName);

    public TList? Load()
    {
        lock (_gate)
        {
            Refresh();
            return _cached;
        }
    }

    public string? StoredSha256()
    {
        lock (_gate)
        {
            Refresh();
            return _cached is null ? null : _cachedSha256;
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
            _hasCache = false;
            _cached = null;
            _cachedSha256 = null;
        }
    }

    // Re-reads the file only when its timestamp changed since the last
    // read; a missing or unreadable file is no list.
    private void Refresh()
    {
        try
        {
            if (!File.Exists(_path))
            {
                Forget();
                return;
            }
            var stamp = File.GetLastWriteTimeUtc(_path);
            if (_hasCache && stamp == _cachedStamp) return;
            var bytes = File.ReadAllBytes(_path);
            _cached = _definition.TryParseList(Encoding.UTF8.GetString(bytes), out var list, out _) ? list : null;
            _cachedSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            _cachedStamp = stamp;
            _hasCache = true;
        }
        catch (IOException)
        {
            Forget();
        }
        catch (UnauthorizedAccessException)
        {
            Forget();
        }
    }

    private void Forget()
    {
        _hasCache = false;
        _cached = null;
        _cachedSha256 = null;
    }
}

// <app data>\catalogue\shortages\shortages-it.json
// (docs/notes/EVOLUTION-PROPOSALS-2.md §3.3).
public sealed class JsonFileShortageListStore : JsonFileDatedListStore<ShortageList>, IShortageListStore
{
    public JsonFileShortageListStore(IAppDataLocation appData)
        : this(PathIn(appData, "shortages", "shortages-it.json"))
    {
    }

    internal JsonFileShortageListStore(string path)
        : base(ShortageFeedDefinition.Instance, path)
    {
    }
}

// <app data>\catalogue\equivalents\equivalents-it.json
// (docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md §2.4).
public sealed class JsonFileEquivalenceListStore : JsonFileDatedListStore<EquivalenceList>, IEquivalenceListStore
{
    public JsonFileEquivalenceListStore(IAppDataLocation appData)
        : this(PathIn(appData, "equivalents", "equivalents-it.json"))
    {
    }

    internal JsonFileEquivalenceListStore(string path)
        : base(EquivalenceFeedDefinition.Instance, path)
    {
    }
}
