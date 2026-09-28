using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.OneDrive;
using Microsoft.Extensions.Options;

namespace MedReminder.Infrastructure.Backup;

// The IArchiveStorage of the automatic cloud backup and of the restore
// dialog (C.3++ Phase 2, docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md
// §7.5): BackupSettings.CloudProvider picks the folder (C.3+) or the
// OneDrive app folder of the configured account. Read on every call, so a
// change in Settings applies without a restart.
internal sealed class CloudArchiveStorage : IArchiveStorage
{
    private readonly IOptionsMonitor<BackupSettings> _settings;
    private readonly LocalFolderArchiveStorage _folder;
    private readonly OneDriveClientFactory _oneDrive;

    public CloudArchiveStorage(
        IOptionsMonitor<BackupSettings> settings,
        LocalFolderArchiveStorage folder,
        OneDriveClientFactory oneDrive)
    {
        _settings = settings;
        _folder = folder;
        _oneDrive = oneDrive;
    }

    public Task<string> UploadAsync(Stream archive, string suggestedName, CancellationToken ct)
        => Current().UploadAsync(archive, suggestedName, ct);

    public Task<Stream> DownloadAsync(string id, CancellationToken ct) => Current().DownloadAsync(id, ct);

    public Task<IReadOnlyList<ArchiveInfo>> ListAsync(CancellationToken ct) => Current().ListAsync(ct);

    public Task DeleteAsync(string id, CancellationToken ct) => Current().DeleteAsync(id, ct);

    private IArchiveStorage Current()
    {
        var settings = _settings.CurrentValue;
        if (settings.CloudProvider is null) return _folder;
        if (settings.CloudProvider != CloudProvider.OneDrive)
        {
            throw new NotSupportedException($"Backup provider {settings.CloudProvider} is not supported.");
        }
        if (string.IsNullOrWhiteSpace(settings.CloudAccountId))
        {
            throw new InvalidOperationException("No OneDrive account is configured for the cloud backup.");
        }
        // Stateless over the shared per-account client (OneDriveClientFactory).
        return new OneDriveArchiveStorage(_oneDrive.Get(settings.CloudAccountId));
    }
}
