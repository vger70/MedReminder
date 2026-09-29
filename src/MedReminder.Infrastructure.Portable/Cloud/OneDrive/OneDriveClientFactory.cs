using System.Collections.Concurrent;

namespace MedReminder.Infrastructure.Cloud.OneDrive;

// One OneDriveClient per account for the process (B.1 Phase 4a), shared
// by the sync transport (SyncTransportFactory) and the cloud backup
// (CloudArchiveStorage), so both use the same HttpClient, token source
// and clock, and tests inject them in one place. A client holds no state
// of its own beyond those, so sharing it is safe. OneDrive needs a token
// source from the host; without one (tests, a host with no app
// registration) Get throws NotSupportedException.
public sealed class OneDriveClientFactory
{
    private readonly IOneDriveAccessTokens? _tokens;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, OneDriveClient> _clients = new(StringComparer.Ordinal);

    public OneDriveClientFactory(IOneDriveAccessTokens? tokens = null, CloudHttp? http = null, TimeProvider? clock = null)
    {
        _tokens = tokens;
        _http = (http ?? CloudHttp.Shared).Client;
        _clock = clock ?? TimeProvider.System;
    }

    public OneDriveClient Get(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var tokens = _tokens ?? throw new NotSupportedException("OneDrive is not available in this build.");
        return _clients.GetOrAdd(accountId, id => new OneDriveClient(
            _http, (force, ct) => tokens.GetAccessTokenAsync(id, force, ct), _clock));
    }
}
