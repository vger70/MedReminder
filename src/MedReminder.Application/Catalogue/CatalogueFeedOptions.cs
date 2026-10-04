namespace MedReminder.Application.Catalogue;

// Remote catalogue feeds (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md
// §6, ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md §5.1), bound from the
// `Catalogue:RemoteFeed` section of appsettings.json. Off unless the
// section enables it; the shipped appsettings.json does. The URLs are
// configurable so tests and forks can point elsewhere.
//
// Each feed lives under `{BaseUrl}{country lower}/`: the manifest is
// `latest.json`, the archive `{prefix}-{version}.zip`.
public sealed class CatalogueFeedOptions
{
    public const string SectionName = "Catalogue:RemoteFeed";

    public const string DefaultBaseUrl = "https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/";

    private const long MiB = 1024L * 1024;

    public bool Enabled { get; set; }

    // Ends with '/'; a missing one is added when the URLs are built.
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    // Keyed by country code. A feed absent from the dictionary is off.
    // The binder merges configured keys into these defaults.
    public Dictionary<string, CatalogueFeedSourceOptions> Feeds { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IT"] = new() { Enabled = true, MaxDownloadBytes = 64 * MiB },
        ["EU"] = new() { Enabled = true, MaxDownloadBytes = 16 * MiB },
        ["ES"] = new() { Enabled = true, MaxDownloadBytes = 16 * MiB },
        ["FR"] = new() { Enabled = true, MaxDownloadBytes = 16 * MiB },
    };

    // Italy-only overrides kept from the single-feed configuration of
    // PR #131: when set, they win over the URLs built from BaseUrl.
    public string? ManifestUrl { get; set; }

    // `{version}` is replaced by the manifest's yyyymm version.
    public string? SnapshotUrlTemplate { get; set; }

    public int ManifestTimeoutSeconds { get; set; } = 10;

    // Shortage list (docs/notes/EVOLUTION-PROPOSALS-2.md §3.3), under
    // `{BaseUrl}it/shortages/`. Downloaded when the remote feeds run and
    // the reference country is Italy.
    public bool ShortagesEnabled { get; set; } = true;

    public long ShortagesMaxDownloadBytes { get; set; } = 4 * MiB;

    // Equivalent medicines list (AIFA "Lista di trasparenza";
    // docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md §2.4), under
    // `{BaseUrl}it/equivalents/`. Same conditions as the shortage list.
    public bool EquivalentsEnabled { get; set; } = true;

    public long EquivalentsMaxDownloadBytes { get; set; } = 4 * MiB;

    // Regional prescription services list (docs/prompt/
    // PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.1), under
    // `{BaseUrl}it/regional-services/`. Same conditions as the shortage
    // list; a few kilobytes.
    public bool RegionalServicesEnabled { get; set; } = true;

    public long RegionalServicesMaxDownloadBytes { get; set; } = 256 * 1024;

    // Folder of an Italian dated list (shortages, equivalents, regional
    // services), ending
    // with '/'. `feed` comes from a DatedListFeedDefinition, not from data.
    public string ItalianFeedFolder(string feed)
    {
        var baseUrl = BaseUrl ?? string.Empty;
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }
        return baseUrl + "it/" + feed + "/";
    }

    public int DownloadTimeoutSeconds { get; set; } = 120;

    public bool IsFeedEnabled(CatalogueFeedDescriptor feed) =>
        Feeds.TryGetValue(feed.Country.Value, out var source) && source.Enabled;

    // Zero when the feed has no configuration: every download is refused.
    public long MaxDownloadBytesFor(CatalogueFeedDescriptor feed) =>
        Feeds.TryGetValue(feed.Country.Value, out var source) ? source.MaxDownloadBytes : 0;

    public string ManifestUrlFor(CatalogueFeedDescriptor feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (IsItaly(feed) && !string.IsNullOrWhiteSpace(ManifestUrl))
        {
            return ManifestUrl;
        }
        return FolderFor(feed) + "latest.json";
    }

    // `version` must already be validated as yyyymm (the manifest parser
    // does it), so the substitution cannot inject a path or query.
    public string SnapshotUrlFor(CatalogueFeedDescriptor feed, string version)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (IsItaly(feed) && !string.IsNullOrWhiteSpace(SnapshotUrlTemplate))
        {
            return SnapshotUrlTemplate.Replace("{version}", version, StringComparison.Ordinal);
        }
        return FolderFor(feed) + feed.FileNameFor(version);
    }

    private static bool IsItaly(CatalogueFeedDescriptor feed) => feed.Country == CatalogueFeedDescriptor.Italy.Country;

    private string FolderFor(CatalogueFeedDescriptor feed)
    {
        var baseUrl = BaseUrl ?? string.Empty;
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }
        return baseUrl + feed.Country.Value.ToLowerInvariant() + "/";
    }
}

// Per-feed settings under `Catalogue:RemoteFeed:Feeds:<country>`.
public sealed class CatalogueFeedSourceOptions
{
    public bool Enabled { get; set; }

    public long MaxDownloadBytes { get; set; } = 16L * 1024 * 1024;
}
