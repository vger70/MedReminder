using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Catalogue;

public enum ShortageRefreshOutcome
{
    ManifestUnavailable,
    UpToDate,
    Updated,
    Rejected,
    Failed,
}

// Downloads the shortage list when the feed publishes a newer one than
// the list stored on this PC (docs/notes/EVOLUTION-PROPOSALS-2.md §3.3).
// Checked before it is stored: size and SHA-256 against the manifest,
// then the content. Every failure is logged and reported through the
// outcome, never thrown: the app must stay usable offline. Run by the
// daily remote-feed step, under the same settings as the catalogues.
public sealed class ShortageRefresher
{
    private readonly IShortageFeedClient _feed;
    private readonly IShortageListStore _store;
    private readonly ILogger<ShortageRefresher> _log;

    public ShortageRefresher(IShortageFeedClient feed, IShortageListStore store, ILogger<ShortageRefresher> log)
    {
        _feed = feed;
        _store = store;
        _log = log;
    }

    public async Task<ShortageRefreshOutcome> RunAsync(CancellationToken cancellationToken)
    {
        ShortageFeedManifest? manifest;
        try
        {
            manifest = await _feed.GetManifestAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "Shortage feed: reading the manifest failed.");
            manifest = null;
        }
        if (manifest is null)
        {
            _log.LogInformation("Shortage feed: manifest unavailable; list left as is.");
            return ShortageRefreshOutcome.ManifestUnavailable;
        }

        var stored = _store.Load();
        if (stored is not null && stored.ListDate >= manifest.ListDate)
        {
            _log.LogInformation("Shortage feed: up to date (list of {ListDate}).", stored.ListDate);
            return ShortageRefreshOutcome.UpToDate;
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
            _log.LogWarning(ex, "Shortage feed: download of {Version} failed; list unchanged.", manifest.Version);
            return ShortageRefreshOutcome.Failed;
        }

        if (bytes.LongLength != manifest.Size
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), manifest.Sha256, StringComparison.Ordinal))
        {
            _log.LogWarning("Shortage feed: {Version} does not match its manifest; list unchanged.", manifest.Version);
            return ShortageRefreshOutcome.Rejected;
        }
        if (!ShortageFeedParser.TryParseList(Encoding.UTF8.GetString(bytes), out var list, out var error)
            || list!.ListDate != manifest.ListDate)
        {
            _log.LogWarning("Shortage feed: {Version} rejected ({Error}); list unchanged.",
                manifest.Version, error ?? "list date differs from the manifest");
            return ShortageRefreshOutcome.Rejected;
        }

        try
        {
            _store.Save(bytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Shortage feed: storing {Version} failed; list unchanged.", manifest.Version);
            return ShortageRefreshOutcome.Failed;
        }
        _log.LogInformation("Shortage feed: list of {ListDate} stored ({Count} entries).", list.ListDate, list.Count);
        return ShortageRefreshOutcome.Updated;
    }
}
