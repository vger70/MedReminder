using System.Runtime.Versioning;
using MedReminder.Application.Export;
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
