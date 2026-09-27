using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Cloud.OneDrive;

// ISyncTransport over the OneDrive app folder (B.1 Phase 4a, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §5.8). Sync files live under `sync/` in the
// app folder; backups (OneDriveArchiveStorage) under `backups/`.
//
// Listing: a folder-by-folder walk costs one request per folder, about
// 600 ms each (spike S6 C14). The transport keeps an index of the app
// folder fed by Graph's delta feed instead: after the first full
// enumeration a refresh is one request when nothing changed. The instance
// is kept for the application's lifetime (SyncTransportFactory), so the
// index survives across sync runs.
//
// The delta feed can lag behind this device's own writes, so the
// transport also remembers what it created or deleted itself until the
// feed agrees (or ten minutes pass): a device always lists its own files.
//
// A create retried after a lost response can report "exists" for a file
// it wrote itself; the engine then publishes the same operations under
// the next number, which replays as a no-op (§5.2).
public sealed class OneDriveSyncTransport : ISyncTransport
{
    public const string RootFolder = "sync";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OverlayLifetime = TimeSpan.FromMinutes(10);

    private readonly OneDriveClient _client;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (bool Present, DateTimeOffset At)> _overlay = new(StringComparer.Ordinal);
    private string? _rootId;
    private string? _deltaLink;
    private DateTimeOffset _refreshedAt = DateTimeOffset.MinValue;

    public OneDriveSyncTransport(OneDriveClient client, TimeProvider? clock = null)
    {
        _client = client;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_clock.GetUtcNow() - _refreshedAt >= RefreshInterval) await RefreshAsync(cancellationToken);

            var files = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (id, node) in _nodes)
            {
                if (node.IsFolder || node.Name.StartsWith('.')) continue;
                if (PathOf(id) is { } path && path.StartsWith(RootFolder + "/", StringComparison.Ordinal))
                {
                    files.Add(path[(RootFolder.Length + 1)..]);
                }
            }

            var now = _clock.GetUtcNow();
            foreach (var (path, (present, at)) in _overlay.ToList())
            {
                if (files.Contains(path) == present || now - at > OverlayLifetime)
                {
                    _overlay.Remove(path);
                    continue;
                }
                if (present) files.Add(path);
                else files.Remove(path);
            }

            return [.. files.Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken)
        => _client.ReadAsync(Remote(path), cancellationToken);

    public async Task<bool> CreateAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var created = await _client.CreateAsync(Remote(path), content, cancellationToken) is not null;
        await RememberAsync(path, present: true, cancellationToken);
        return created;
    }

    public async Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        await _client.WriteAsync(Remote(path), content, cancellationToken);
        await RememberAsync(path, present: true, cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        await _client.DeleteAsync(Remote(path), cancellationToken);
        await RememberAsync(path, present: false, cancellationToken);
    }

    private async Task RememberAsync(string path, bool present, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _overlay[path] = (present, _clock.GetUtcNow());
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        _rootId ??= await _client.GetRootIdAsync(ct);
        try
        {
            await ApplyDeltaAsync(ct);
        }
        catch (DeltaExpiredException)
        {
            // The cursor is too old: enumerate everything again.
            _nodes.Clear();
            _deltaLink = null;
            await ApplyDeltaAsync(ct);
        }
        _refreshedAt = _clock.GetUtcNow();
    }

    private async Task ApplyDeltaAsync(CancellationToken ct)
    {
        var link = _deltaLink;
        while (true)
        {
            var page = await _client.DeltaAsync(link, ct);
            foreach (var item in page.Items)
            {
                if (item.Deleted) _nodes.Remove(item.Id);
                else if (item.Name is not null) _nodes[item.Id] = new Node(item.Name, item.ParentId, item.IsFolder);
            }
            if (page.NextLink is not null)
            {
                link = page.NextLink;
                continue;
            }
            // Only a complete feed moves the cursor.
            _deltaLink = page.DeltaLink ?? _deltaLink;
            return;
        }
    }

    // Path relative to the app folder; null when the chain is broken (an
    // item whose parent was deleted, or outside the app folder).
    private string? PathOf(string id)
    {
        var segments = new List<string>();
        var current = id;
        for (var depth = 0; depth < 32; depth++)
        {
            if (current == _rootId)
            {
                segments.Reverse();
                return string.Join('/', segments);
            }
            if (!_nodes.TryGetValue(current, out var node) || node.ParentId is null) return null;
            segments.Add(node.Name);
            current = node.ParentId;
        }
        return null;
    }

    private static string Remote(string path)
    {
        OneDriveClient.ValidatePath(path);
        return $"{RootFolder}/{path}";
    }

    private sealed record Node(string Name, string? ParentId, bool IsFolder);
}
