using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Catalogue;

public enum EquivalenceRefreshOutcome
{
    ManifestUnavailable,
    UpToDate,
    Updated,
    Rejected,
    Failed,
}

// Downloads the equivalents list when the feed publishes a newer one
// than the list stored on this PC
// (docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md §2.4).
// Checked before it is stored: size and SHA-256 against the manifest,
// then the content. Every failure is logged and reported through the
// outcome, never thrown: the app must stay usable offline. Run by the
// daily remote-feed step, under the same settings as the catalogues.
public sealed class EquivalenceRefresher
{
    private readonly IEquivalenceFeedClient _feed;
    private readonly IEquivalenceListStore _store;
    private readonly ILogger<EquivalenceRefresher> _log;

    public EquivalenceRefresher(IEquivalenceFeedClient feed, IEquivalenceListStore store, ILogger<EquivalenceRefresher> log)
    {
        _feed = feed;
        _store = store;
        _log = log;
    }

    public async Task<EquivalenceRefreshOutcome> RunAsync(CancellationToken cancellationToken)
    {
        EquivalenceFeedManifest? manifest;
        try
        {
            manifest = await _feed.GetManifestAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "Equivalents feed: reading the manifest failed.");
            manifest = null;
        }
        if (manifest is null)
        {
            _log.LogInformation("Equivalents feed: manifest unavailable; list left as is.");
            return EquivalenceRefreshOutcome.ManifestUnavailable;
        }

        var stored = _store.Load();
        if (stored is not null && stored.ListDate >= manifest.ListDate)
        {
            _log.LogInformation("Equivalents feed: up to date (list of {ListDate}).", stored.ListDate);
            return EquivalenceRefreshOutcome.UpToDate;
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
            _log.LogWarning(ex, "Equivalents feed: download of {Version} failed; list unchanged.", manifest.Version);
            return EquivalenceRefreshOutcome.Failed;
        }

        if (bytes.LongLength != manifest.Size
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), manifest.Sha256, StringComparison.Ordinal))
        {
            _log.LogWarning("Equivalents feed: {Version} does not match its manifest; list unchanged.", manifest.Version);
            return EquivalenceRefreshOutcome.Rejected;
        }
        if (!EquivalenceFeedParser.TryParseList(Encoding.UTF8.GetString(bytes), out var list, out var error)
            || list!.ListDate != manifest.ListDate)
        {
            _log.LogWarning("Equivalents feed: {Version} rejected ({Error}); list unchanged.",
                manifest.Version, error ?? "list date differs from the manifest");
            return EquivalenceRefreshOutcome.Rejected;
        }

        try
        {
            _store.Save(bytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Equivalents feed: storing {Version} failed; list unchanged.", manifest.Version);
            return EquivalenceRefreshOutcome.Failed;
        }
        _log.LogInformation("Equivalents feed: list of {ListDate} stored ({Groups} groups, {Packages} packages).",
            list.ListDate, list.GroupCount, list.PackageCount);
        return EquivalenceRefreshOutcome.Updated;
    }
}
