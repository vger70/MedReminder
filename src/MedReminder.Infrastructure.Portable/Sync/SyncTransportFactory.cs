using System.Collections.Concurrent;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud;
using MedReminder.Infrastructure.Cloud.OneDrive;

namespace MedReminder.Infrastructure.Sync;

// Builds the transport of a sync target (B.1 Phase 4a). Singleton: one
// OneDrive transport per account is kept, so its change-feed index lives
// across sync runs. OneDrive needs a token source from the host; without
// one (tests, a host with no app registration) only folders are
// supported.
public sealed class SyncTransportFactory : ISyncTransportFactory
{
    private readonly IOneDriveAccessTokens? _oneDrive;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, OneDriveSyncTransport> _oneDriveTransports = new(StringComparer.Ordinal);

    public SyncTransportFactory(IOneDriveAccessTokens? oneDrive = null, CloudHttp? http = null, TimeProvider? clock = null)
    {
        _oneDrive = oneDrive;
        _http = (http ?? CloudHttp.Shared).Client;
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
                var tokens = _oneDrive ?? throw new NotSupportedException("OneDrive is not available in this build.");
                return _oneDriveTransports.GetOrAdd(accountId, id => new OneDriveSyncTransport(
                    new OneDriveClient(_http, (force, ct) => tokens.GetAccessTokenAsync(id, force, ct), _clock), _clock));
            default:
                throw new NotSupportedException($"Sync provider {target.Provider} is not supported.");
        }
    }
}
