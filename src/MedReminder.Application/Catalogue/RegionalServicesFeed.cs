using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Prescriptions;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Catalogue;

// The regional services feed (docs/prompt/
// PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.1): the health record
// service of each Italian region, maintained by hand in
// scripts/feeds/regional_services_it.json and published by
// scripts/feeds/regional_services.py under data/it/regional-services/ as
// `regional-services-<yyyymmdd>.json` (the source's listDate, moved on
// every change) with a `latest.json` manifest. Same transport, store and
// refresh as the shortage list (DatedListFeed.cs); a copy ships with the
// application.
public sealed class RegionalServicesFeedDefinition : DatedListFeedDefinition<RegionalServicesList>
{
    public static readonly RegionalServicesFeedDefinition Instance = new();

    private RegionalServicesFeedDefinition()
    {
    }

    public override string Name => "Regional services";

    public override string Folder => "regional-services";

    public override string FilePrefix => RegionalServicesFeedParser.FilePrefix;

    public override long MaxDownloadBytes(CatalogueFeedOptions options) => options.RegionalServicesMaxDownloadBytes;

    public override bool TryParseList(string json, [NotNullWhen(true)] out RegionalServicesList? list, out string? error)
        => RegionalServicesFeedParser.TryParseList(json, out list, out error);

    public override DateOnly ListDateOf(RegionalServicesList list) => list.ListDate;

    public override string Describe(RegionalServicesList list) => $"{list.Count} services";
}

// Pure parsers, testable without HTTP. Unknown fields are ignored. The
// same rules as the publisher's validator (scripts/feeds/
// regional_services.py), in both directions: a list the publisher
// accepts is never refused here (every client would keep a stale list or
// none), and a hand-edited copy the publisher would refuse is refused.
public static class RegionalServicesFeedParser
{
    public const string Country = DatedFeedManifestParser.Country;
    public const string FilePrefix = "regional-services-";
    private const int MaxText = 200;

    public static bool TryParseManifest(string json, out DatedFeedManifest? manifest, out string? error)
        => DatedFeedManifestParser.TryParse(json, FilePrefix, out manifest, out error);

    // Rejects a list for another country, an unknown or repeated region
    // code, a URL that is not https, an unknown sign-in method or a
    // missing field.
    public static bool TryParseList(string json, [NotNullWhen(true)] out RegionalServicesList? list, out string? error)
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
            if (!root.TryGetProperty("services", out var services) || services.ValueKind != JsonValueKind.Array)
                return Fail("The list has no 'services'.", out error);

            var parsed = new List<RegionalHealthService>(services.GetArrayLength());
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in services.EnumerateArray())
            {
                var code = s.ValueKind == JsonValueKind.Object ? String(s, "regionCode") : null;
                if (!ItalianRegions.IsValid(code)) return Fail("An entry has no valid 'regionCode'.", out error);
                if (!seen.Add(code!)) return Fail($"Region {code} appears twice.", out error);
                if (Text(s, "region") is not { } region || Text(s, "service") is not { } service)
                    return Fail($"Region {code} has no 'region' or 'service'.", out error);
                if (Https(s, "webUrl") is not { } web) return Fail($"Region {code} has no https 'webUrl'.", out error);
                if (!OptionalHttps(s, "iosAppUrl", out var ios) || !OptionalHttps(s, "androidAppUrl", out var android))
                    return Fail($"Region {code} has an app link that is not https.", out error);
                if (SignIn(s) is not { } signIn) return Fail($"Region {code} has no valid 'signIn'.", out error);
                if (Bool(s, "showsPrescriptions") is not { } shows || Bool(s, "familyDelegation") is not { } delegation)
                    return Fail($"Region {code} has no boolean 'showsPrescriptions' or 'familyDelegation'.", out error);
                if (Date(s, "verifiedOn") is not { } verifiedOn)
                    return Fail($"Region {code} has no valid 'verifiedOn'.", out error);
                parsed.Add(new RegionalHealthService(code!, region, service, web, ios, android, signIn, shows, delegation,
                    verifiedOn));
            }
            list = new RegionalServicesList(CountryCode.Parse(Country), listDate, parsed);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            return Fail("Malformed list: " + ex.Message, out error);
        }
    }

    private static IReadOnlyList<SignInMethod>? SignIn(JsonElement element)
    {
        if (!element.TryGetProperty("signIn", out var values) || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() == 0)
            return null;
        var methods = new List<SignInMethod>();
        foreach (var value in values.EnumerateArray())
        {
            SignInMethod? method = value.ValueKind == JsonValueKind.String
                ? value.GetString() switch
                {
                    "SPID" => SignInMethod.Spid,
                    "CIE" => SignInMethod.Cie,
                    "TS-CNS" => SignInMethod.TsCns,
                    _ => null,
                }
                : null;
            if (method is not { } m || methods.Contains(m)) return null;
            methods.Add(m);
        }
        return methods;
    }

    // An absolute https URL: printable ASCII only, an ASCII DNS host with
    // a dot, no credentials, no port other than 443.
    internal static Uri? ParseHttps(string? text)
        => text is { Length: > 0 }
           && text.All(c => c is > ' ' and < '\u007F')
           && Uri.TryCreate(text, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && uri.IsDefaultPort
           && !Authority(text).Contains('@')
           && HostPattern.IsMatch(uri.Host)
            ? uri
            : null;

    // What follows "https://" up to the path, query or fragment.
    private static string Authority(string url)
    {
        var rest = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var end = rest.IndexOfAny(['/', '?', '#']);
        return end < 0 ? rest : rest[..end];
    }

    // The publisher's HOST pattern: an ASCII DNS name with at least one dot.
    private static readonly Regex HostPattern = new(
        "^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z][a-z0-9-]{0,61}[a-z0-9]$",
        RegexOptions.CultureInvariant);

    private static Uri? Https(JsonElement element, string name) => ParseHttps(String(element, name));

    private static bool OptionalHttps(JsonElement element, string name, out Uri? uri)
    {
        uri = null;
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return true;
        uri = ParseHttps(value.ValueKind == JsonValueKind.String ? value.GetString() : null);
        return uri is not null;
    }

    private static string? Text(JsonElement element, string name)
        => String(element, name) is { Length: > 0 and <= MaxText } text && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static bool? Bool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

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

// Transport of the regional services feed.
public interface IRegionalServicesFeedClient : IDatedListFeedClient<RegionalServicesList>
{
}

// The regional services list kept on this PC, shared by every profile:
// the downloaded list, or the copy shipped with the application when it
// is newer or nothing was downloaded.
public interface IRegionalServicesListStore : IDatedListStore<RegionalServicesList>
{
}

// Refresh of the regional services list, with the shortage list.
public sealed class RegionalServicesRefresher : DatedListRefresher<RegionalServicesList>
{
    public RegionalServicesRefresher(IRegionalServicesFeedClient feed, IRegionalServicesListStore store,
        ILogger<RegionalServicesRefresher> log)
        : base(RegionalServicesFeedDefinition.Instance, feed, store, log)
    {
    }
}

// Which of two lists a profile reads: the downloaded one, unless the
// application ships a newer copy (an update installed after the last
// download). Both carry the source's listDate, which the publisher
// requires to move on every change, so the same date means the same
// content and keeps the downloaded one.
public static class RegionalServicesListChoice
{
    public static RegionalServicesList? Newest(RegionalServicesList? downloaded, RegionalServicesList? shipped)
    {
        if (downloaded is null) return shipped;
        if (shipped is null) return downloaded;
        return shipped.ListDate > downloaded.ListDate ? shipped : downloaded;
    }
}
