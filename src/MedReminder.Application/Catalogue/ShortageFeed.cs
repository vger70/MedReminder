using System.Globalization;
using System.Text.Json;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// The shortage feed (docs/notes/EVOLUTION-PROPOSALS-2.md §3.3): the AIFA
// list of medicines in temporary shortage, published by
// scripts/feeds/aifa_shortages.py under data/it/shortages/ as
// `shortages-<yyyymmdd>.json` with a `latest.json` manifest. Versioned
// by the list date, because AIFA updates the list several times a month.
public sealed record ShortageFeedManifest(string Version, string File, string Sha256, long Size)
{
    // The list date the version stands for.
    public DateOnly ListDate => DateOnly.ParseExact(Version, "yyyyMMdd", CultureInfo.InvariantCulture);
}

// Pure parsers, testable without HTTP. Unknown fields are ignored.
public static class ShortageFeedParser
{
    public const string Country = "IT";
    public const string FilePrefix = "shortages-";

    public static bool TryParseManifest(string json, out ShortageFeedManifest? manifest, out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);
        manifest = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("The manifest is not a JSON object.", out error);
            if (String(root, "country") != Country) return Fail($"The manifest is not for {Country}.", out error);
            var version = String(root, "version");
            if (version is null || !DateOnly.TryParseExact(version, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
                return Fail("The manifest has no valid 'version' (yyyymmdd).", out error);
            var file = String(root, "file");
            if (file != FilePrefix + version + ".json")
                return Fail($"The manifest 'file' does not match '{FilePrefix}{version}.json'.", out error);
            var sha = String(root, "sha256");
            if (sha is not { Length: 64 } || !sha.All(char.IsAsciiHexDigit))
                return Fail("The manifest 'sha256' is not 64 hexadecimal characters.", out error);
            if (!root.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number
                || !size.TryGetInt64(out var bytes) || bytes <= 0)
                return Fail("The manifest 'size' is not a positive integer.", out error);
            manifest = new ShortageFeedManifest(version, file, sha.ToLowerInvariant(), bytes);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            return Fail("Malformed manifest: " + ex.Message, out error);
        }
    }

    // The list a feed file holds. Rejects a file for another country, a
    // missing or malformed field, or a code that is not 9 digits.
    public static bool TryParseList(string json, out ShortageList? list, out string? error)
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

// Transport of the shortage feed. Never throws from GetManifestAsync
// except on cancellation; DownloadAsync throws on any failure.
public interface IShortageFeedClient
{
    Task<ShortageFeedManifest?> GetManifestAsync(CancellationToken cancellationToken);

    Task<byte[]> DownloadAsync(ShortageFeedManifest manifest, CancellationToken cancellationToken);
}

// The shortage list kept on this PC, shared by every profile (it is
// public reference data, not profile data).
public interface IShortageListStore
{
    // Null when no list was stored yet or the stored file is unreadable.
    ShortageList? Load();

    // Stores a list file already checked by ShortageFeedParser.
    void Save(byte[] listJson);
}
