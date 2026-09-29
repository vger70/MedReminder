using System.Diagnostics;
using System.IO.Compression;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Catalogue;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Catalogue;

public enum RemoteCatalogueRefreshOutcome
{
    // The manifest could not be fetched or parsed.
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

// Downloads the AIFA snapshot published by the remote feed and imports
// it into the open profile's database when it is newer than the one
// already there (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md §4.2).
//
// The archive is staged under <app data>\catalogue\staging\ and
// deleted in every outcome; leftovers of an interrupted run are removed
// at the start of every run, before the network is touched. Every
// failure is logged and reported through the outcome, never thrown:
// the app must stay usable offline.
//
// The import runs under WriteGate: its transaction replaces the whole
// Italian catalogue and holds the SQLite write lock meanwhile, so the
// use cases wait for it instead of failing with SQLITE_BUSY after the
// busy timeout.
// Only the open profile is updated; other profiles refresh at their own
// next start (§4.4).
public sealed class RemoteCatalogueRefresher
{
    // Zip-bomb guard on the two CSV entries (§5.4). The AIFA pair is
    // about 94 MB uncompressed today.
    public const long MaxUncompressedBytes = 512L * 1024 * 1024;

    private const string ConfezioniEntryName = "confezioni_fornitura.csv";
    private const string PaEntryName = "PA_confezioni.csv";

    private static readonly CountryCode Italy = CountryCode.Parse("IT");

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

    public async Task<RemoteCatalogueRefreshOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var staging = GetStagingDirectory(_appData.DataDirectory);
        ClearStaging(staging);

        CatalogueFeedManifest? manifest;
        try
        {
            manifest = await _feed.GetLatestAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The port promises not to throw; guard anyway so a transport
            // bug still ends as a logged outcome.
            _log.LogWarning(ex, "Remote AIFA feed: reading the manifest failed.");
            manifest = null;
        }

        if (manifest is null)
        {
            _log.LogInformation("Remote AIFA feed: manifest unavailable; catalogue left as is.");
            return RemoteCatalogueRefreshOutcome.ManifestUnavailable;
        }

        CatalogueImportState state;
        try
        {
            state = await _importer.GetImportStateAsync(Italy, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "Remote AIFA feed: could not read the imported catalogue version.");
            return RemoteCatalogueRefreshOutcome.Failed;
        }

        if (!SnapshotVersion.IsNewer(manifest.Version, state.Version))
        {
            _log.LogInformation(
                "Remote AIFA feed: up to date (local={Local}, remote={Remote}).",
                state.Version, manifest.Version);
            return RemoteCatalogueRefreshOutcome.UpToDate;
        }

        _log.LogInformation(
            "Remote AIFA feed: newer snapshot available (local={Local}, remote={Remote}).",
            state.Version ?? "none", manifest.Version);

        var target = Path.Combine(staging, manifest.ExpectedFileName);
        try
        {
            Directory.CreateDirectory(staging);
            return await DownloadAndImportAsync(manifest, state, target, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            _log.LogWarning(ex, "Remote AIFA feed: snapshot {Version} rejected; catalogue unchanged.", manifest.Version);
            return RemoteCatalogueRefreshOutcome.Rejected;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Remote AIFA feed: refresh to {Version} failed; catalogue unchanged.", manifest.Version);
            return RemoteCatalogueRefreshOutcome.Failed;
        }
        finally
        {
            TryDelete(target + ".part");
            TryDelete(target);
        }
    }

    private async Task<RemoteCatalogueRefreshOutcome> DownloadAndImportAsync(
        CatalogueFeedManifest manifest,
        CatalogueImportState state,
        string target,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var download = await _feed.DownloadAsync(manifest, target, cancellationToken);
        _log.LogInformation(
            "Remote AIFA feed: downloaded {Bytes} bytes in {Elapsed} ms (sha256 {HashPrefix}…).",
            download.Length, stopwatch.ElapsedMilliseconds, download.Sha256[..12]);

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
            _log.LogInformation("Remote AIFA feed: the manifest carries no sha256; the download was not hash-verified.");
        }

        await using var snapshot = new FileStream(
            target, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

        ValidateArchive(snapshot);
        snapshot.Position = 0;

        // A month that loses half of the packages is a broken feed,
        // not data (decision D4).
        var minimumRowCount = Math.Max(1, state.RowCount / 2);
        stopwatch.Restart();
        var report = await WriteGate.RunExclusiveAsync(
            ct => _importer.ImportAsync(snapshot, Italy, manifest.Version, minimumRowCount, ct),
            cancellationToken);

        _log.LogInformation(
            "Reference-catalogue import for {Country} complete: inserted={Inserted}, deleted={Deleted}, skipped={Skipped}, version={Version}, completedAt={CompletedAt}, source=remote feed, elapsedMs={Elapsed}.",
            Italy.Value, report.Inserted, report.Deleted, report.Skipped,
            report.SnapshotVersion, report.CompletedAt, stopwatch.ElapsedMilliseconds);

        return RemoteCatalogueRefreshOutcome.Imported;
    }

    // Structural checks before the importer reads the archive (§5.4):
    // a readable ZIP holding both AIFA CSV entries, within the
    // uncompressed-size cap. Column checks stay in the parser.
    public static void ValidateArchive(Stream archiveStream)
    {
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);

        var confezioni = FindEntry(archive, ConfezioniEntryName);
        var pa = FindEntry(archive, PaEntryName);

        var total = confezioni.Length + pa.Length;
        if (total > MaxUncompressedBytes)
        {
            throw new InvalidDataException(
                $"The AIFA CSV entries expand to {total} bytes, above the {MaxUncompressedBytes}-byte limit.");
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

    private void ClearStaging(string staging)
    {
        try
        {
            if (!Directory.Exists(staging))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(staging))
            {
                TryDelete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Remote AIFA feed: could not list the staging folder; the next start retries.");
        }
    }

    private void TryDelete(string path)
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
            _log.LogWarning(ex, "Remote AIFA feed: could not delete a staging file; the next start retries.");
        }
    }
}
