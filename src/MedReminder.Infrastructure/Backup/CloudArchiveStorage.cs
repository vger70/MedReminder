using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.GoogleDrive;
using MedReminder.Infrastructure.Cloud.OneDrive;
using Microsoft.Extensions.Options;

namespace MedReminder.Infrastructure.Backup;

// The IArchiveStorage of the automatic cloud backup and of the restore
// dialog (C.3++ Phase 2, docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md
// §7.5): BackupSettings.CloudProvider picks the folder (C.3+), the
// OneDrive app folder (Phase 4a) or the MedReminder/backups folder in
// Google Drive (Phase 4b) of the configured account. Read on every call, so a
// change in Settings applies without a restart.
internal sealed class CloudArchiveStorage : IArchiveStorage
{
    private readonly IOptionsMonitor<BackupSettings> _settings;
    private readonly LocalFolderArchiveStorage _folder;
    private readonly OneDriveClientFactory _oneDrive;
    private readonly GoogleDriveClientFactory _googleDrive;

    public CloudArchiveStorage(
        IOptionsMonitor<BackupSettings> settings,
        LocalFolderArchiveStorage folder,
        OneDriveClientFactory oneDrive,
        GoogleDriveClientFactory googleDrive)
    {
        _settings = settings;
        _folder = folder;
        _oneDrive = oneDrive;
        _googleDrive = googleDrive;
    }

    public Task<string> UploadAsync(Stream archive, string suggestedName, CancellationToken ct)
        => Current().UploadAsync(archive, suggestedName, ct);

    public Task<Stream> DownloadAsync(string id, CancellationToken ct) => Current().DownloadAsync(id, ct);

    public Task<IReadOnlyList<ArchiveInfo>> ListAsync(CancellationToken ct) => Current().ListAsync(ct);

    public Task DeleteAsync(string id, CancellationToken ct) => Current().DeleteAsync(id, ct);

    private IArchiveStorage Current()
    {
        var settings = _settings.CurrentValue;
        if (settings.CloudProvider is not { } provider) return _folder;
        if (string.IsNullOrWhiteSpace(settings.CloudAccountId))
        {
            throw new InvalidOperationException($"No {provider} account is configured for the cloud backup.");
        }
        // Stateless over the shared per-account clients.
        return provider switch
        {
            CloudProvider.OneDrive => new OneDriveArchiveStorage(_oneDrive.Get(settings.CloudAccountId)),
            CloudProvider.GoogleDrive => new GoogleDriveArchiveStorage(_googleDrive.Get(settings.CloudAccountId)),
            _ => throw new NotSupportedException($"Backup provider {provider} is not supported."),
        };
    }
}
