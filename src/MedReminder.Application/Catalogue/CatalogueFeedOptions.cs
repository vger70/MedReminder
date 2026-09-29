namespace MedReminder.Application.Catalogue;

// Remote AIFA feed (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md §6),
// bound from the `Catalogue:RemoteFeed` section of appsettings.json.
// Off unless the section enables it; the shipped appsettings.json does.
// The URLs are configurable so tests and forks can point elsewhere.
public sealed class CatalogueFeedOptions
{
    public const string SectionName = "Catalogue:RemoteFeed";

    public const string DefaultManifestUrl =
        "https://raw.githubusercontent.com/vger70/MedReminder/main/data/latest.json";

    public const string DefaultSnapshotUrlTemplate =
        "https://raw.githubusercontent.com/vger70/MedReminder/main/data/aifa-{version}.zip";

    public bool Enabled { get; set; }

    public string ManifestUrl { get; set; } = DefaultManifestUrl;

    // `{version}` is replaced by the manifest's yyyymm version.
    public string SnapshotUrlTemplate { get; set; } = DefaultSnapshotUrlTemplate;

    public int ManifestTimeoutSeconds { get; set; } = 10;

    public int DownloadTimeoutSeconds { get; set; } = 120;

    public long MaxDownloadBytes { get; set; } = 64L * 1024 * 1024;
}
