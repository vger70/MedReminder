using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Cloud.GoogleDrive;

// ISyncTransport over the Google Drive app data folder (B.1 Phase 4b,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5.8, spike S7 in §18.7). The
// folder is hidden from the user and shared by every OAuth client of the
// project (S7 C15), so desktop and phone see the same files.
//
// Drive has ids, not paths, and creating nested folders costs one request
// each (S7 C2). The files are therefore flat in the app data folder: the
// name is the relative path of SYNC-FORMAT.md §2, and a public property
// marks the sync files, so one paged query lists them all (S7 C16, C18).
// The listing is refreshed at most every few seconds; the query results
// lag a few seconds behind writes (S7 C8, C17), so the transport also
// remembers its own creates and deletes until the listing agrees.
//
// Create-only: Drive accepts duplicate names (S7 C4). A create checks the
// name, creates with a pre-generated id (a retried create is recognised,
// C4b), then checks again; among files that share a name the oldest wins
// on every device (GoogleFile.Winner), and a loser deletes its own copy.
// In the sync layout every file has one writer except group.json and a
// new genesis, so a real race needs two devices creating the same group
// or generation at the same second.
public sealed class GoogleDriveSyncTransport : ISyncTransport
{
    public const string SyncProperty = "mrsync";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OverlayLifetime = TimeSpan.FromMinutes(10);
    private static readonly IReadOnlyDictionary<string, string> Properties
        = new Dictionary<string, string> { [SyncProperty] = "1" };

    private readonly GoogleDriveClient _client;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Path → files with that name (usually one).
    private Dictionary<string, List<GoogleFile>> _index = new(StringComparer.Ordinal);
    // Own writes (Id set) and deletes (Id null) not yet seen in the listing.
    private readonly Dictionary<string, (string? Id, DateTimeOffset At)> _overlay = new(StringComparer.Ordinal);
    private DateTimeOffset _refreshedAt = DateTimeOffset.MinValue;

    public GoogleDriveSyncTransport(GoogleDriveClient client, TimeProvider? clock = null)
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
            return [.. Paths().Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        Validate(path);
        var id = await ResolveAsync(path, cancellationToken);
        return id is null ? null : await _client.ReadAsync(id, cancellationToken);
    }

    public async Task<bool> CreateAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        Validate(path);
        ArgumentNullException.ThrowIfNull(content);
        if ((await ByNameAsync(path, cancellationToken)).Count > 0) return false;

        var id = await _client.GenerateIdAsync(GoogleDriveClient.AppDataFolder, cancellationToken);
        var created = await _client.CreateAsync(id, path, GoogleDriveClient.AppDataFolder, Properties,
            new MemoryStream(content, writable: false), content.Length, cancellationToken);

        // Another writer may have created the same name meanwhile.
        var winner = GoogleFile.Winner(await ByNameAsync(path, cancellationToken)) ?? created;
        if (winner.Id != created.Id)
        {
            await _client.DeleteAsync(created.Id, cancellationToken);
            return false;
        }
        await RememberAsync(path, created.Id, cancellationToken);
        return true;
    }

    public async Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        Validate(path);
        ArgumentNullException.ThrowIfNull(content);
        var existing = GoogleFile.Winner(await ByNameAsync(path, cancellationToken));
        if (existing is not null)
        {
            await _client.UpdateContentAsync(existing.Id, new MemoryStream(content, writable: false), content.Length,
                cancellationToken);
            await RememberAsync(path, existing.Id, cancellationToken);
            return;
        }
        var id = await _client.GenerateIdAsync(GoogleDriveClient.AppDataFolder, cancellationToken);
        await _client.CreateAsync(id, path, GoogleDriveClient.AppDataFolder, Properties,
            new MemoryStream(content, writable: false), content.Length, cancellationToken);
        await RememberAsync(path, id, cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        Validate(path);
        foreach (var file in await ByNameAsync(path, cancellationToken))
        {
            await _client.DeleteAsync(file.Id, cancellationToken);
        }
        await RememberAsync(path, null, cancellationToken);
    }

    // The winning file of a path: this device's own write first (the
    // listing may not show it yet), then the listing, then one query.
    private async Task<string?> ResolveAsync(string path, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_overlay.TryGetValue(path, out var own)) return own.Id;
            if (_index.TryGetValue(path, out var files)) return GoogleFile.Winner(files)?.Id;
        }
        finally
        {
            _gate.Release();
        }
        return GoogleFile.Winner(await ByNameAsync(path, ct))?.Id;
    }

    private Task<IReadOnlyList<GoogleFile>> ByNameAsync(string path, CancellationToken ct)
        => _client.QueryAsync(
            $"name = {GoogleDriveClient.Literal(path)} and {GoogleDriveClient.PropertyClause(SyncProperty, "1")} and trashed = false",
            GoogleDriveClient.AppDataFolder, ct);

    private async Task RefreshAsync(CancellationToken ct)
    {
        var files = await _client.QueryAsync(
            $"{GoogleDriveClient.PropertyClause(SyncProperty, "1")} and trashed = false", GoogleDriveClient.AppDataFolder, ct);
        _index = files.GroupBy(f => f.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        _refreshedAt = _clock.GetUtcNow();
    }

    private HashSet<string> Paths()
    {
        var paths = new HashSet<string>(_index.Keys, StringComparer.Ordinal);
        var now = _clock.GetUtcNow();
        foreach (var (path, (id, at)) in _overlay.ToList())
        {
            var listed = _index.TryGetValue(path, out var files) && (id is null || files.Any(f => f.Id == id));
            if (listed == (id is not null) || now - at > OverlayLifetime)
            {
                _overlay.Remove(path);
                continue;
            }
            if (id is not null) paths.Add(path);
            else paths.Remove(path);
        }
        return paths;
    }

    private async Task RememberAsync(string path, string? id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _overlay[path] = (id, _clock.GetUtcNow());
        }
        finally
        {
            _gate.Release();
        }
    }

    // Relative, '/'-separated, no empty, '.' or '..' segment.
    private static void Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\\') || path.Split('/').Any(s => s.Length == 0 || s is "." or ".."))
        {
            throw new ArgumentException("Invalid remote path.", nameof(path));
        }
    }
}
