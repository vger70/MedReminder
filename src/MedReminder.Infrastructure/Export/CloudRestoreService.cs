using System.Runtime.Versioning;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Export;

// Enumerates encrypted .mrz snapshots in a cloud-synced folder and
// delegates the actual decrypt-and-apply to IImportService
// (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §4.4). No separate
// import pipeline: C.3+ reuses C.3's format and services verbatim so
// user-triggered exports and automatic snapshots stay interchangeable.
[SupportedOSPlatform("windows")]
internal sealed class CloudRestoreService : ICloudRestoreService
{
    private readonly IImportService _importService;
    private readonly ILogger<CloudRestoreService> _log;

    public CloudRestoreService(
        IImportService importService,
        ILogger<CloudRestoreService> log)
    {
        _importService = importService;
        _log = log;
    }

    public async Task<IReadOnlyList<CloudSnapshotInfo>> ListSnapshotsAsync(
        string folderPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        if (!Directory.Exists(folderPath))
        {
            return Array.Empty<CloudSnapshotInfo>();
        }

        var results = new List<CloudSnapshotInfo>();
        foreach (var path in Directory.EnumerateFiles(folderPath, "*.mrz"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExportManifest manifest;
            try
            {
                manifest = await _importService.ReadManifestAsync(path, cancellationToken);
            }
            catch (Exception ex)
            {
                // A malformed or wrong-version archive is not surfaced as
                // a failure — the Restore dialog only lists archives the
                // user can act on (§4.4). The path is logged so an
                // admin can look up why a snapshot was omitted.
                _log.LogWarning(ex, "Skipping cloud snapshot {Path}: unreadable manifest.", path);
                continue;
            }

            results.Add(new CloudSnapshotInfo
            {
                ArchivePath = path,
                FileName = Path.GetFileName(path),
                CreatedAtUtc = manifest.CreatedAtUtc,
                ProfileId = manifest.ProfileId ?? string.Empty,
                DeviceHostHash = manifest.Device?.HostNameSha256 ?? string.Empty,
                Source = manifest.Source ?? string.Empty,
                AppVersion = manifest.AppVersion,
            });
        }

        results.Sort((a, b) => b.CreatedAtUtc.CompareTo(a.CreatedAtUtc));
        return results;
    }

    public Task<IReadOnlyList<CloudSnapshotInfo>> ListStoredSnapshotsAsync(
        IArchiveStorage storage, CancellationToken cancellationToken)
        => CloudSnapshots.ListAsync(storage, cancellationToken);

    public async Task<bool> RestoreStoredAsync(
        IArchiveStorage storage,
        string archiveId,
        char[] passphrase,
        ImportOptions options,
        Func<ExportManifest, bool>? confirmManifest,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);
        // Under %LOCALAPPDATA%\MedReminder (CLAUDE.md §5), deleted after use.
        var temp = Path.Combine(AppDataPaths.GetAppDataDirectory(), $"restore-{Guid.NewGuid():N}.mrz");
        try
        {
            await using (var source = await storage.DownloadAsync(archiveId, cancellationToken))
            await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(target, cancellationToken);
            }
            if (confirmManifest is not null)
            {
                var manifest = await _importService.ReadManifestAsync(temp, cancellationToken);
                if (!confirmManifest(manifest)) return false;
            }
            await _importService.ImportAsync(temp, passphrase, options, progress, cancellationToken);
            return true;
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (IOException ex)
            {
                _log.LogWarning(ex, "Could not delete the downloaded snapshot {Path}.", temp);
            }
        }
    }

    public Task RestoreAsync(
        string archivePath,
        char[] passphrase,
        ImportOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        return _importService.ImportAsync(archivePath, passphrase, options, progress, cancellationToken);
    }
}
