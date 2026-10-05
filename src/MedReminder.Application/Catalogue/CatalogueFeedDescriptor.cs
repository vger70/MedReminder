using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// One remote catalogue feed: what its archive is called and what it
// must contain (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md
// §5.2). The URLs and the download cap come from CatalogueFeedOptions;
// the archive layout is fixed by the parser for the country, so it is
// not configurable.
public sealed class CatalogueFeedDescriptor
{
    private const long MiB = 1024L * 1024;

    public static CatalogueFeedDescriptor Italy { get; } = new(
        CountryCode.Parse("IT"), "aifa", ["confezioni_fornitura.csv", "PA_confezioni.csv"], 512 * MiB);

    public static CatalogueFeedDescriptor EuropeanUnion { get; } = new(
        CountryCode.Parse("EU"), "ema-epar", ["ema-epar.csv"], 64 * MiB);

    public static CatalogueFeedDescriptor Spain { get; } = new(
        CountryCode.Parse("ES"), "aemps", ["aemps.xlsx"], 64 * MiB);

    public static CatalogueFeedDescriptor France { get; } = new(
        CountryCode.Parse("FR"), "bdpm", ["CIS_bdpm.txt", "CIS_COMPO_bdpm.txt"], 64 * MiB);

    // Remote feed only, no embedded snapshot
    // (docs/analysis/ANALYSIS-CATALOGUE-US-GB-SOURCES.md §3.3, D3).
    public static CatalogueFeedDescriptor UnitedStates { get; } = new(
        CountryCode.Parse("US"), "fda-ndc", ["fda-ndc.tsv"], 256 * MiB);

    // Every known feed, in refresh order.
    public static IReadOnlyList<CatalogueFeedDescriptor> All { get; } = [Italy, EuropeanUnion, Spain, France, UnitedStates];

    private CatalogueFeedDescriptor(
        CountryCode country, string prefix, IReadOnlyList<string> requiredEntries, long maxUncompressedBytes)
    {
        Country = country;
        Prefix = prefix;
        RequiredEntries = requiredEntries;
        MaxUncompressedBytes = maxUncompressedBytes;
    }

    public CountryCode Country { get; }

    public string Prefix { get; }

    // Entry names the archive must hold, compared case-insensitively on
    // the file name alone (any folder), as the parsers do.
    public IReadOnlyList<string> RequiredEntries { get; }

    // Zip-bomb guard on the sum of the required entries' uncompressed sizes.
    public long MaxUncompressedBytes { get; }

    public string FileNameFor(string version) => $"{Prefix}-{version}.zip";

    public override string ToString() => Country.Value;
}
