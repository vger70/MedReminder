using System.Collections.Concurrent;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.GoogleDrive;
using MedReminder.Infrastructure.Cloud.OneDrive;

namespace MedReminder.Infrastructure.Sync;

// Builds the transport of a sync target (B.1 Phase 4a, 4b). Singleton: one
// provider transport per account is kept, so its index lives across sync
// runs. The OneDrive clients come from OneDriveClientFactory,
// shared with the cloud backup; without one (tests, a host with no app
// registration) only folders are supported.
public sealed class SyncTransportFactory : ISyncTransportFactory
{
    private readonly OneDriveClientFactory? _oneDrive;
    private readonly GoogleDriveClientFactory? _googleDrive;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, OneDriveSyncTransport> _oneDriveTransports = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GoogleDriveSyncTransport> _googleDriveTransports = new(StringComparer.Ordinal);

    public SyncTransportFactory(OneDriveClientFactory? oneDrive = null, TimeProvider? clock = null,
        GoogleDriveClientFactory? googleDrive = null)
    {
        _oneDrive = oneDrive;
        _googleDrive = googleDrive;
        _clock = clock ?? TimeProvider.System;
    }

    public ISyncTransport Create(SyncTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        switch (target.Provider)
        {
            case null:
                return new LocalFolderSyncTransport(target.Folder
                    ?? throw new InvalidOperationException("No sync folder is configured for this profile."));
            case CloudProvider.OneDrive:
                var accountId = target.AccountId
                    ?? throw new InvalidOperationException("No OneDrive account is configured for this profile.");
                var clients = _oneDrive ?? throw new NotSupportedException("OneDrive is not available in this build.");
                return _oneDriveTransports.GetOrAdd(accountId, id => new OneDriveSyncTransport(clients.Get(id), _clock));
            case CloudProvider.GoogleDrive:
                var googleAccount = target.AccountId
                    ?? throw new InvalidOperationException("No Google account is configured for this profile.");
                var google = _googleDrive ?? throw new NotSupportedException("Google Drive is not available in this build.");
                return _googleDriveTransports.GetOrAdd(googleAccount, id => new GoogleDriveSyncTransport(google.Get(id), _clock));
            default:
                throw new NotSupportedException($"Sync provider {target.Provider} is not supported.");
        }
    }
}
