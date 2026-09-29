using System.Collections.Concurrent;

namespace MedReminder.Infrastructure.Cloud.GoogleDrive;

// One GoogleDriveClient per account for the process (B.1 Phase 4b), shared
// by the sync transport and the cloud backup, like OneDriveClientFactory.
// Without a token source from the host Get throws NotSupportedException.
public sealed class GoogleDriveClientFactory
{
    private readonly IGoogleDriveAccessTokens? _tokens;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, GoogleDriveClient> _clients = new(StringComparer.Ordinal);

    public GoogleDriveClientFactory(IGoogleDriveAccessTokens? tokens = null, CloudHttp? http = null, TimeProvider? clock = null)
    {
        _tokens = tokens;
        _http = (http ?? CloudHttp.Shared).Client;
        _clock = clock ?? TimeProvider.System;
    }

    public GoogleDriveClient Get(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var tokens = _tokens ?? throw new NotSupportedException("Google Drive is not available in this build.");
        return _clients.GetOrAdd(accountId, id => new GoogleDriveClient(
            _http, (force, ct) => tokens.GetAccessTokenAsync(id, force, ct), _clock));
    }
}
