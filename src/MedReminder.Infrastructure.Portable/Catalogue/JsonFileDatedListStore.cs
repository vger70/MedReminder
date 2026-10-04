using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Prescriptions;

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

// <app data>\catalogue\regional-services\regional-services-it.json
// (docs/prompt/PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.1), with the
// copy of scripts/feeds/regional_services_it.json shipped in this
// assembly for the first start and for an installation that never
// downloads feeds. Load returns the newer of the two
// (RegionalServicesListChoice); Save writes the downloaded file only.
public sealed class JsonFileRegionalServicesListStore : IRegionalServicesListStore
{
    internal const string ShippedResource = "MedReminder.Infrastructure.Assets.regional-services-it.json";

    private readonly DownloadedStore _downloaded;
    private readonly Lazy<(RegionalServicesList? List, string? Sha256)> _shipped;

    public JsonFileRegionalServicesListStore(IAppDataLocation appData)
        : this(Path.Combine(appData.DataDirectory, "catalogue", "regional-services", "regional-services-it.json"),
            ReadShipped)
    {
    }

    // Test constructor: shipped returns the bytes of the shipped copy, or null.
    internal JsonFileRegionalServicesListStore(string path, Func<byte[]?> shipped)
    {
        _downloaded = new DownloadedStore(path);
        _shipped = new Lazy<(RegionalServicesList?, string?)>(() =>
        {
            var bytes = shipped();
            if (bytes is null) return (null, null);
            return RegionalServicesFeedDefinition.Instance.TryParseList(Encoding.UTF8.GetString(bytes), out var list, out _)
                ? (list, Convert.ToHexStringLower(SHA256.HashData(bytes)))
                : (null, null);
        });
    }

    public RegionalServicesList? Load()
        => RegionalServicesListChoice.Newest(_downloaded.Load(), _shipped.Value.List);

    // The SHA-256 of the list Load returns, so the refresher downloads a
    // republished file of the same date.
    public string? StoredSha256()
    {
        var downloaded = _downloaded.Load();
        var chosen = RegionalServicesListChoice.Newest(downloaded, _shipped.Value.List);
        if (chosen is null) return null;
        return ReferenceEquals(chosen, downloaded) ? _downloaded.StoredSha256() : _shipped.Value.Sha256;
    }

    public void Save(byte[] listJson) => _downloaded.Save(listJson);

    private static byte[]? ReadShipped()
    {
        using var stream = typeof(JsonFileRegionalServicesListStore).Assembly.GetManifestResourceStream(ShippedResource);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed class DownloadedStore(string path)
        : JsonFileDatedListStore<RegionalServicesList>(RegionalServicesFeedDefinition.Instance, path);
}
