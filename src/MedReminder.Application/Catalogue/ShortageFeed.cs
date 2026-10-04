using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using MedReminder.Domain.Catalogue;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Catalogue;

// The shortage feed (docs/notes/EVOLUTION-PROPOSALS-2.md §3.3): the AIFA
// list of medicines in temporary shortage, published by
// scripts/feeds/aifa_shortages.py under data/it/shortages/ as
// `shortages-<yyyymmdd>.json` with a `latest.json` manifest. Versioned
// by the list date, because AIFA updates the list several times a month.
// Transport, store and refresh are the shared dated-list ones
// (DatedListFeed.cs).
public sealed class ShortageFeedDefinition : DatedListFeedDefinition<ShortageList>
{
    public static readonly ShortageFeedDefinition Instance = new();

    private ShortageFeedDefinition()
    {
    }

    public override string Name => "Shortage";

    public override string Folder => "shortages";

    public override string FilePrefix => ShortageFeedParser.FilePrefix;

    public override long MaxDownloadBytes(CatalogueFeedOptions options) => options.ShortagesMaxDownloadBytes;

    public override bool TryParseList(string json, [NotNullWhen(true)] out ShortageList? list, out string? error)
        => ShortageFeedParser.TryParseList(json, out list, out error);

    public override DateOnly ListDateOf(ShortageList list) => list.ListDate;

    public override string Describe(ShortageList list) => $"{list.Count} entries";
}

// Pure parsers, testable without HTTP. Unknown fields are ignored.
public static class ShortageFeedParser
{
    public const string Country = DatedFeedManifestParser.Country;
    public const string FilePrefix = "shortages-";

    public static bool TryParseManifest(string json, out DatedFeedManifest? manifest, out string? error)
        => DatedFeedManifestParser.TryParse(json, FilePrefix, out manifest, out error);

    // The list a feed file holds. Rejects a file for another country, a
    // missing or malformed field, or a code that is not 9 digits.
    public static bool TryParseList(string json, [NotNullWhen(true)] out ShortageList? list, out string? error)
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
            if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return Fail("The list has no 'entries'.", out error);

            var parsed = new List<ShortageEntry>(entries.GetArrayLength());
            foreach (var e in entries.EnumerateArray())
            {
                var code = e.ValueKind == JsonValueKind.Object ? String(e, "aic") : null;
                if (code is not { Length: 9 } || !code.All(char.IsAsciiDigit))
                    return Fail("An entry has no valid 'aic'.", out error);
                if (Date(e, "start") is not { } start) return Fail($"Entry {code} has no valid 'start'.", out error);
                DateOnly? end = null;
                if (e.TryGetProperty("expectedEnd", out var endElement) && endElement.ValueKind != JsonValueKind.Null)
                {
                    end = Date(e, "expectedEnd") ?? throw new JsonException($"Entry {code} has an invalid 'expectedEnd'.");
                }
                var equivalent = e.TryGetProperty("equivalent", out var eq) && eq.ValueKind == JsonValueKind.True;
                parsed.Add(new ShortageEntry(code, start, end, equivalent, Reason(String(e, "reason"))));
            }
            list = new ShortageList(CountryCode.Parse(Country), listDate, parsed);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            return Fail("Malformed list: " + ex.Message, out error);
        }
    }

    private static ShortageReason Reason(string? value) => value switch
    {
        "production" => ShortageReason.Production,
        "demand" => ShortageReason.Demand,
        "withdrawn" => ShortageReason.Withdrawn,
        "suspended" => ShortageReason.Suspended,
        "commercial" => ShortageReason.Commercial,
        "regulatory" => ShortageReason.Regulatory,
        _ => ShortageReason.Other,
    };

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

// Transport of the shortage feed.
public interface IShortageFeedClient : IDatedListFeedClient<ShortageList>
{
}

// The shortage list kept on this PC, shared by every profile.
public interface IShortageListStore : IDatedListStore<ShortageList>
{
}

// Refresh of the shortage list (EVOLUTION-PROPOSALS-2 §3.3).
public sealed class ShortageRefresher : DatedListRefresher<ShortageList>
{
    public ShortageRefresher(IShortageFeedClient feed, IShortageListStore store, ILogger<ShortageRefresher> log)
        : base(ShortageFeedDefinition.Instance, feed, store, log)
    {
    }
}
