using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using MedReminder.Domain.Catalogue;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Catalogue;

// The equivalents feed (docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md
// §2.4): the AIFA "Lista di trasparenza", published by
// scripts/feeds/aifa_equivalents.py under data/it/equivalents/ as
// `equivalents-<yyyymmdd>.json` with a `latest.json` manifest. Versioned
// by the list date, as the shortage feed, with the same transport, store
// and refresh (DatedListFeed.cs).
public sealed class EquivalenceFeedDefinition : DatedListFeedDefinition<EquivalenceList>
{
    public static readonly EquivalenceFeedDefinition Instance = new();

    private EquivalenceFeedDefinition()
    {
    }

    public override string Name => "Equivalents";

    public override string Folder => "equivalents";

    public override string FilePrefix => EquivalenceFeedParser.FilePrefix;

    public override long MaxDownloadBytes(CatalogueFeedOptions options) => options.EquivalentsMaxDownloadBytes;

    public override bool TryParseList(string json, [NotNullWhen(true)] out EquivalenceList? list, out string? error)
        => EquivalenceFeedParser.TryParseList(json, out list, out error);

    public override DateOnly ListDateOf(EquivalenceList list) => list.ListDate;

    public override string Describe(EquivalenceList list) => $"{list.GroupCount} groups, {list.PackageCount} packages";
}

// Pure parsers, testable without HTTP. Unknown fields are ignored.
public static class EquivalenceFeedParser
{
    public const string Country = DatedFeedManifestParser.Country;
    public const string FilePrefix = "equivalents-";

    public static bool TryParseManifest(string json, out DatedFeedManifest? manifest, out string? error)
        => DatedFeedManifestParser.TryParse(json, FilePrefix, out manifest, out error);

    // The list a feed file holds. Rejects a file for another country, a
    // group without code or members, a code that is not 9 digits, or a
    // price that is not a whole number of cents.
    public static bool TryParseList(string json, [NotNullWhen(true)] out EquivalenceList? list, out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);
        list = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("The list is not a JSON object.", out error);
            if (String(root, "country") != Country) return Fail($"The list is not for {Country}.", out error);
            if (Date(root, "listDate") is not { } listDate) return Fail("The list has no valid 'listDate'.", out error);
            if (!root.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
                return Fail("The list has no 'groups'.", out error);

            var parsed = new List<EquivalenceGroup>(groups.GetArrayLength());
            foreach (var g in groups.EnumerateArray())
            {
                var code = g.ValueKind == JsonValueKind.Object ? String(g, "code") : null;
                if (string.IsNullOrWhiteSpace(code)) return Fail("A group has no 'code'.", out error);
                if (!g.TryGetProperty("members", out var members) || members.ValueKind != JsonValueKind.Array
                    || members.GetArrayLength() == 0)
                    return Fail($"Group {code} has no 'members'.", out error);

                var packages = new List<EquivalentPackage>(members.GetArrayLength());
                foreach (var m in members.EnumerateArray())
                {
                    var aic = m.ValueKind == JsonValueKind.Object ? String(m, "aic") : null;
                    if (aic is not { Length: 9 } || !aic.All(char.IsAsciiDigit))
                        return Fail($"A member of group {code} has no valid 'aic'.", out error);
                    packages.Add(new EquivalentPackage(
                        aic,
                        String(m, "name") ?? string.Empty,
                        String(m, "package") ?? string.Empty,
                        String(m, "holder") ?? string.Empty,
                        Euros(m, "price"),
                        Euros(m, "difference"),
                        String(m, "note") is { Length: > 0 } note ? note : null));
                }
                parsed.Add(new EquivalenceGroup(
                    code,
                    String(g, "ingredient") ?? string.Empty,
                    String(g, "reference") ?? string.Empty,
                    String(g, "atc") is { Length: > 0 } atc ? atc : null,
                    Euros(g, "referencePrice"),
                    packages));
            }
            list = new EquivalenceList(CountryCode.Parse(Country), listDate, parsed);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            return Fail("Malformed list: " + ex.Message, out error);
        }
    }

    // A price in cents, as published, in euros; null when absent.
    private static decimal? Euros(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var cents))
            throw new JsonException($"'{name}' is not a whole number of cents.");
        return cents / 100m;
    }

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateOnly? Date(JsonElement element, string name)
        => String(element, name) is { } text
           && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}

// Transport of the equivalents feed.
public interface IEquivalenceFeedClient : IDatedListFeedClient<EquivalenceList>
{
}

// The equivalents list kept on this PC, shared by every profile.
public interface IEquivalenceListStore : IDatedListStore<EquivalenceList>
{
}

// Refresh of the equivalents list (ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK §2.4).
public sealed class EquivalenceRefresher : DatedListRefresher<EquivalenceList>
{
    public EquivalenceRefresher(IEquivalenceFeedClient feed, IEquivalenceListStore store, ILogger<EquivalenceRefresher> log)
        : base(EquivalenceFeedDefinition.Instance, feed, store, log)
    {
    }
}
