using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Catalogue;

// The Italian reference lists that are not catalogues: the AIFA shortage
// list (docs/notes/EVOLUTION-PROPOSALS-2.md §3.3) and the AIFA
// transparency list (docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md
// §2.4). Each is one JSON file published under `{BaseUrl}it/<folder>/` as
// `<prefix>-<yyyymmdd>.json` (the list date) with a `latest.json`
// manifest, downloaded with Italy as reference country and stored once
// for every profile. The transport, the store and the refresh are shared;
// a definition gives what differs between the lists.
public sealed record DatedFeedManifest(string Version, string File, string Sha256, long Size)
{
    // The list date the version stands for.
    public DateOnly ListDate => DateOnly.ParseExact(Version, "yyyyMMdd", CultureInfo.InvariantCulture);
}

// What differs between two dated lists.
public abstract class DatedListFeedDefinition<TList> where TList : class
{
    // Used in log lines only ("Shortage feed: ...").
    public abstract string Name { get; }

    // Folder under `{BaseUrl}it/`.
    public abstract string Folder { get; }

    // File name prefix, dash included ("shortages-").
    public abstract string FilePrefix { get; }

    public abstract long MaxDownloadBytes(CatalogueFeedOptions options);

    public abstract bool TryParseList(string json, [NotNullWhen(true)] out TList? list, out string? error);

    public abstract DateOnly ListDateOf(TList list);

    // Size of the list for the log line, without personal data.
    public abstract string Describe(TList list);

    public bool TryParseManifest(string json, out DatedFeedManifest? manifest, out string? error)
        => DatedFeedManifestParser.TryParse(json, FilePrefix, out manifest, out error);
}

// Pure parser of a `latest.json` manifest, testable without HTTP.
// Unknown fields are ignored.
public static class DatedFeedManifestParser
{
    public const string Country = "IT";

    public static bool TryParse(string json, string filePrefix, out DatedFeedManifest? manifest, out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrEmpty(filePrefix);
        const string country = Country;
        manifest = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("The manifest is not a JSON object.", out error);
            if (String(root, "country") != country) return Fail($"The manifest is not for {country}.", out error);
            var version = String(root, "version");
            if (version is null || !DateOnly.TryParseExact(version, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
                return Fail("The manifest has no valid 'version' (yyyymmdd).", out error);
            var file = String(root, "file");
            if (file != filePrefix + version + ".json")
                return Fail($"The manifest 'file' does not match '{filePrefix}{version}.json'.", out error);
            var sha = String(root, "sha256");
            if (sha is not { Length: 64 } || !sha.All(char.IsAsciiHexDigit))
                return Fail("The manifest 'sha256' is not 64 hexadecimal characters.", out error);
            if (!root.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number
                || !size.TryGetInt64(out var bytes) || bytes <= 0)
                return Fail("The manifest 'size' is not a positive integer.", out error);
            manifest = new DatedFeedManifest(version, file, sha.ToLowerInvariant(), bytes);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            return Fail("Malformed manifest: " + ex.Message, out error);
        }
    }

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}

// Transport of a dated list. Never throws from GetManifestAsync except on
// cancellation; DownloadAsync throws on any failure.
public interface IDatedListFeedClient<TList> where TList : class
{
    Task<DatedFeedManifest?> GetManifestAsync(CancellationToken cancellationToken);

    Task<byte[]> DownloadAsync(DatedFeedManifest manifest, CancellationToken cancellationToken);
}

// A dated list kept on this PC, shared by every profile (public reference
// data, not profile data).
public interface IDatedListStore<TList> where TList : class
{
    // Null when no list was stored yet or the stored file is unreadable.
    TList? Load();

    // SHA-256 (lower-case hex) of the stored file when it holds a valid
    // list, else null: tells a republished file of the same list date.
    string? StoredSha256();

    // Stores a list file already checked by its definition.
    void Save(byte[] listJson);
}

public enum DatedListRefreshOutcome
{
    ManifestUnavailable,
    UpToDate,
    Updated,
    Rejected,
    Failed,
}

// Downloads a dated list when the feed publishes a newer one than the
// list stored on this PC, or the same list date republished with other
// content (a forced run of the workflow, after a fix). Checked before it
// is stored: size and SHA-256 against the manifest, then the content.
// Every failure is logged and reported through the outcome, never thrown:
// the app must stay usable offline. Run by the daily remote-feed step,
// under the same settings as the catalogues.
public class DatedListRefresher<TList> where TList : class
{
    private readonly DatedListFeedDefinition<TList> _definition;
    private readonly IDatedListFeedClient<TList> _feed;
    private readonly IDatedListStore<TList> _store;
    private readonly ILogger _log;

    public DatedListRefresher(
        DatedListFeedDefinition<TList> definition,
        IDatedListFeedClient<TList> feed,
        IDatedListStore<TList> store,
        ILogger log)
    {
        _definition = definition;
        _feed = feed;
        _store = store;
        _log = log;
    }

    public async Task<DatedListRefreshOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var name = _definition.Name;
        DatedFeedManifest? manifest;
        try
        {
            manifest = await _feed.GetManifestAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "{Feed} feed: reading the manifest failed.", name);
            manifest = null;
        }
        if (manifest is null)
        {
            _log.LogInformation("{Feed} feed: manifest unavailable; list left as is.", name);
            return DatedListRefreshOutcome.ManifestUnavailable;
        }

        if (_store.Load() is { } stored)
        {
            var storedDate = _definition.ListDateOf(stored);
            if (storedDate > manifest.ListDate
                || (storedDate == manifest.ListDate
                    && string.Equals(_store.StoredSha256(), manifest.Sha256, StringComparison.Ordinal)))
            {
                _log.LogInformation("{Feed} feed: up to date (list of {ListDate}).", name, storedDate);
                return DatedListRefreshOutcome.UpToDate;
            }
        }

        byte[] bytes;
        try
        {
            bytes = await _feed.DownloadAsync(manifest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{Feed} feed: download of {Version} failed; list unchanged.", name, manifest.Version);
            return DatedListRefreshOutcome.Failed;
        }

        if (bytes.LongLength != manifest.Size
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), manifest.Sha256, StringComparison.Ordinal))
        {
            _log.LogWarning("{Feed} feed: {Version} does not match its manifest; list unchanged.", name, manifest.Version);
            return DatedListRefreshOutcome.Rejected;
        }
        if (!_definition.TryParseList(Encoding.UTF8.GetString(bytes), out var list, out var error)
            || _definition.ListDateOf(list) != manifest.ListDate)
        {
            _log.LogWarning("{Feed} feed: {Version} rejected ({Error}); list unchanged.",
                name, manifest.Version, error ?? "list date differs from the manifest");
            return DatedListRefreshOutcome.Rejected;
        }

        try
        {
            _store.Save(bytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{Feed} feed: storing {Version} failed; list unchanged.", name, manifest.Version);
            return DatedListRefreshOutcome.Failed;
        }
        _log.LogInformation("{Feed} feed: list of {ListDate} stored ({Size}).",
            name, manifest.ListDate, _definition.Describe(list));
        return DatedListRefreshOutcome.Updated;
    }
}
