using System.Diagnostics;
using System.IO.Compression;
using MedReminder.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Catalogue;

public enum RemoteCatalogueRefreshOutcome
{
    // The manifest could not be fetched or parsed, or belongs to another feed.
    ManifestUnavailable,

    // The open profile already holds the published version or a newer one.
    UpToDate,

    // The published snapshot was downloaded and imported.
    Imported,

    // The download or the archive failed a check (size, hash, content,
    // row count); the catalogue is unchanged.
    Rejected,

    // Network, I/O or import error; the catalogue is unchanged.
    Failed,
}

// Downloads the snapshot a remote catalogue feed publishes and imports
// it into the open profile's database when it is newer than the one
// already there (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md §4.2,
// ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md §5.2). The feed descriptor
// supplies the country, the archive name and the required entries.
//
// The archive is staged under <app data>\catalogue\staging\ and
// deleted in every outcome; leftovers of an interrupted run are removed
// at the start of every run, before the network is touched. Every
// failure is logged and reported through the outcome, never thrown:
// the app must stay usable offline.
//
// The import runs under WriteGate: its transaction replaces the whole
// catalogue of the country and holds the SQLite write lock meanwhile,
// so the use cases wait for it instead of failing with SQLITE_BUSY
// after the busy timeout. The database swaps (backup restore, archive
// import, sync join) take the same gate through IDatabaseExclusiveAccess,
// and the importer closes the connection it opened before the gate is
// released, so no handle stays open during the download.
// Only the open profile is updated; other profiles refresh at their own
// next start or daily check (§4.4).
public sealed class RemoteCatalogueRefresher
{
    private readonly ICatalogueFeedClient _feed;
    private readonly IReferenceCatalogueImporter _importer;
    private readonly IAppDataLocation _appData;
    private readonly ILogger<RemoteCatalogueRefresher> _log;

    public RemoteCatalogueRefresher(
        ICatalogueFeedClient feed,
        IReferenceCatalogueImporter importer,
        IAppDataLocation appData,
        ILogger<RemoteCatalogueRefresher> log)
    {
        _feed = feed;
        _importer = importer;
        _appData = appData;
        _log = log;
    }

    public static string GetStagingDirectory(string appDataDirectory) =>
        Path.Combine(appDataDirectory, "catalogue", "staging");

    public async Task<RemoteCatalogueRefreshOutcome> RunAsync(
        CatalogueFeedDescriptor feed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var country = feed.Country.Value;

        var staging = GetStagingDirectory(_appData.DataDirectory);
        ClearStaging(staging, country);

        // The client returns null for a manifest that belongs to another
        // feed (a misconfigured URL pointing at another feed's folder),
        // so it never replaces this country's catalogue.
        CatalogueFeedManifest? manifest;
        try
        {
            manifest = await _feed.GetLatestAsync(feed, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The port promises not to throw; guard anyway so a transport
            // bug still ends as a logged outcome.
            _log.LogWarning(ex, "Remote catalogue feed {Country}: reading the manifest failed.", country);
            manifest = null;
        }

        if (manifest is null)
        {
            _log.LogInformation("Remote catalogue feed {Country}: manifest unavailable; catalogue left as is.", country);
            return RemoteCatalogueRefreshOutcome.ManifestUnavailable;
        }

        CatalogueImportState state;
        try
        {
            // Under the gate like the import: a database swap must not
            // move the file while this read holds it open.
            state = await WriteGate.RunExclusiveAsync(
                ct => _importer.GetImportStateAsync(feed.Country, ct), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "Remote catalogue feed {Country}: could not read the imported catalogue version.", country);
            return RemoteCatalogueRefreshOutcome.Failed;
        }

        var label = LabelFor(manifest);
        if (!SnapshotVersion.IsNewer(label, state.Version))
        {
            _log.LogInformation(
                "Remote catalogue feed {Country}: up to date (local={Local}, remote={Remote}).",
                country, state.Version, label);
            return RemoteCatalogueRefreshOutcome.UpToDate;
        }

        _log.LogInformation(
            "Remote catalogue feed {Country}: newer snapshot available (local={Local}, remote={Remote}).",
            country, state.Version ?? "none", label);

        var target = Path.Combine(staging, feed.FileNameFor(manifest.Version));
        try
        {
            Directory.CreateDirectory(staging);
            return await DownloadAndImportAsync(feed, manifest, label, state, target, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            _log.LogWarning(
                ex, "Remote catalogue feed {Country}: snapshot {Version} rejected; catalogue unchanged.", country, label);
            return RemoteCatalogueRefreshOutcome.Rejected;
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                ex, "Remote catalogue feed {Country}: refresh to {Version} failed; catalogue unchanged.", country, label);
            return RemoteCatalogueRefreshOutcome.Failed;
        }
        finally
        {
            TryDelete(target + ".part", country);
            TryDelete(target, country);
        }
    }

    // The label stored with the imported rows. The build time in the
    // suffix lets an archive republished in the same month replace the
    // earlier one (§11.2). It is used only when the manifest also has a
    // SHA-256: raw.githubusercontent.com caches files for five minutes,
    // so right after a republish the new manifest can arrive with the
    // old archive, and only the hash rejects that pair. Without it the
    // old content would be stored under the new label and never fixed.
    public static string LabelFor(CatalogueFeedManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Sha256 is not null && manifest.Generated is { } generated
            ? SnapshotVersion.Compose(manifest.Version, generated)
            : manifest.Version;
    }

    private async Task<RemoteCatalogueRefreshOutcome> DownloadAndImportAsync(
        CatalogueFeedDescriptor feed,
        CatalogueFeedManifest manifest,
        string label,
        CatalogueImportState state,
        string target,
        CancellationToken cancellationToken)
    {
        var country = feed.Country.Value;
        var stopwatch = Stopwatch.StartNew();
        var download = await _feed.DownloadAsync(feed, manifest, target, cancellationToken);
        _log.LogInformation(
            "Remote catalogue feed {Country}: downloaded {Bytes} bytes in {Elapsed} ms (sha256 {HashPrefix}…).",
            country, download.Length, stopwatch.ElapsedMilliseconds, download.Sha256[..12]);

        if (manifest.Size is { } size && size != download.Length)
        {
            throw new InvalidDataException(
                $"Downloaded {download.Length} bytes; the manifest announces {size}.");
        }

        if (manifest.Sha256 is { } expected)
        {
            if (!string.Equals(expected, download.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The SHA-256 of the download does not match the manifest.");
            }
        }
        else
        {
            _log.LogInformation(
                "Remote catalogue feed {Country}: the manifest carries no sha256; the download was not hash-verified.",
                country);
        }

        await using var snapshot = new FileStream(
            target, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

        ValidateArchive(snapshot, feed);
        snapshot.Position = 0;

        // A month that loses half of the packages is a broken feed,
        // not data (decision D4).
        var minimumRowCount = Math.Max(1, state.RowCount / 2);
        stopwatch.Restart();
        var report = await WriteGate.RunExclusiveAsync(
            ct => _importer.ImportAsync(snapshot, feed.Country, label, minimumRowCount, ct),
            cancellationToken);

        _log.LogInformation(
            "Reference-catalogue import for {Country} complete: inserted={Inserted}, deleted={Deleted}, skipped={Skipped}, version={Version}, completedAt={CompletedAt}, source=remote feed, elapsedMs={Elapsed}.",
            country, report.Inserted, report.Deleted, report.Skipped,
            report.SnapshotVersion, report.CompletedAt, stopwatch.ElapsedMilliseconds);

        return RemoteCatalogueRefreshOutcome.Imported;
    }

    // Structural checks before the importer reads the archive (§5.4):
    // a readable ZIP holding every entry the feed requires, within the
    // feed's uncompressed-size cap. Column checks stay in the parser.
    public static void ValidateArchive(Stream archiveStream, CatalogueFeedDescriptor feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);

        long total = 0;
        foreach (var name in feed.RequiredEntries)
        {
            total += FindEntry(archive, name).Length;
        }

        if (total > feed.MaxUncompressedBytes)
        {
            throw new InvalidDataException(
                $"The {feed.Country.Value} archive entries expand to {total} bytes, above the {feed.MaxUncompressedBytes}-byte limit.");
        }
    }

    private static ZipArchiveEntry FindEntry(ZipArchive archive, string name)
    {
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        throw new InvalidDataException($"The archive has no '{name}' entry.");
    }

    private void ClearStaging(string staging, string country)
    {
        try
        {
            if (!Directory.Exists(staging))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(staging))
            {
                TryDelete(file, country);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(
                ex, "Remote catalogue feed {Country}: could not list the staging folder; the next run retries.", country);
        }
    }

    private void TryDelete(string path, string country)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(
                ex, "Remote catalogue feed {Country}: could not delete a staging file; the next run retries.", country);
        }
    }
}
