using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Application.Sync.Remote;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

// Enables sync on the first device of a profile (B.1 Phase 3c,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5.4, §5.5): genesis register
// versions, a random group key wrapped with the sync passphrase, and the
// genesis image of the current database. From then on the use cases
// record operations. The UI that calls it is Phase 3d.
public sealed class CreateSyncGroup
{
    private readonly SyncGenesis _genesis;
    private readonly ISyncSnapshotStore _snapshots;
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncKeyStore _keys;
    private readonly ISyncPeerRepository _peers;
    private readonly IArchiveCipher _cipher;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly SyncEngineOptions _options;

    public CreateSyncGroup(
        SyncGenesis genesis,
        ISyncSnapshotStore snapshots,
        ISyncSettingsStore settings,
        ISyncKeyStore keys,
        ISyncPeerRepository peers,
        IArchiveCipher cipher,
        IUnitOfWork uow,
        TimeProvider clock,
        SyncEngineOptions? options = null)
    {
        _clock = clock;
        _options = options ?? new SyncEngineOptions();
        _genesis = genesis;
        _snapshots = snapshots;
        _settings = settings;
        _keys = keys;
        _peers = peers;
        _cipher = cipher;
        _uow = uow;
    }

    public async Task<SyncSettings> ExecuteAsync(
        ISyncTransport transport, char[] passphrase, string? folder, CancellationToken cancellationToken,
        Argon2Params? kdf = null, string? deviceName = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(passphrase);
        if (_settings.Load() is not null) throw new InvalidOperationException("Sync is already enabled.");

        await _genesis.RecordAsync(cancellationToken);
        return await WriteGate.RunExclusiveAsync(async ct =>
        {
            var settings = new SyncSettings(Guid.NewGuid(), Guid.NewGuid(), 1, 1, folder, deviceName);
            var key = RandomNumberGenerator.GetBytes(SyncKeyWrap.KeySize);
            try
            {
                if (!await transport.CreateAsync(SyncLayout.GroupFile(settings.GroupId),
                        SyncGroupFile.Create(settings.GroupId).ToBytes(), ct))
                {
                    throw new InvalidOperationException("The sync group already exists.");
                }
                await transport.CreateAsync(SyncLayout.KeyWrap(settings.GroupId, 1),
                    SyncKeyWrap.Wrap(_cipher, settings.GroupId, 1, key, passphrase, kdf ?? Argon2Params.Default).ToBytes(), ct);
                await WriteGenesisAsync(transport, _cipher, _snapshots, settings, key, ct);
                await SyncEngine.WriteRecordAsync(transport, _cipher, key, settings, _options, _clock, 0,
                    new Dictionary<Guid, int>(), ct);

                await _peers.ClearAsync(ct);
                await _peers.AddAsync(new SyncPeer { DeviceId = settings.DeviceId, Generation = 1 }, ct);
                await _uow.SaveChangesAsync(ct);
                _keys.Save(settings.GroupId, 1, key);
                _settings.Save(settings);
                return settings;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }, cancellationToken);
    }

    internal static async Task WriteGenesisAsync(ISyncTransport transport, IArchiveCipher cipher,
        ISyncSnapshotStore snapshots, SyncSettings settings, byte[] key, CancellationToken ct)
    {
        var image = await snapshots.CaptureAsync(ct);
        var header = SyncEngine.Header(SyncFileKind.Genesis, settings, settings.DeviceId, 0, snapshots.SchemaVersion)
            with { Vector = new Dictionary<Guid, int>() };
        if (!await transport.CreateAsync(SyncLayout.Genesis(settings.GroupId, settings.Generation),
                SyncFileCodec.Seal(cipher, key, header, new SnapshotContent(snapshots.SchemaVersion, image).ToBytes()), ct))
        {
            throw new InvalidOperationException($"Generation {settings.Generation} already exists.");
        }
    }
}

public sealed record SyncJoinResult(SyncSettings Settings, byte[] Key);

// Joins an existing group on another device (§5.5): unwrap the group key
// with the sync passphrase, pick the newest image usable for a bootstrap
// and build a new profile database from it. The device never uploads the
// data it had: the caller replaces the local database with the built one
// (after the same confirmation as an import), then saves the returned
// settings and key.
public sealed class JoinSyncGroup
{
    private readonly ISyncSnapshotStore _snapshots;
    private readonly IArchiveCipher _cipher;
    private readonly TimeProvider _clock;
    private readonly SyncEngineOptions _options;

    public JoinSyncGroup(ISyncSnapshotStore snapshots, IArchiveCipher cipher, TimeProvider clock,
        SyncEngineOptions? options = null)
    {
        _snapshots = snapshots;
        _cipher = cipher;
        _clock = clock;
        _options = options ?? new SyncEngineOptions();
    }

    // Groups present in the storage.
    public static async Task<IReadOnlyList<Guid>> ListGroupsAsync(ISyncTransport transport, CancellationToken ct)
        => [.. (await transport.ListAsync(string.Empty, ct))
            .Where(p => p.EndsWith("/group.json", StringComparison.Ordinal))
            .Select(p => Guid.TryParseExact(p[..p.IndexOf('/')], "N", out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)];

    // Throws CryptographicException for a wrong passphrase.
    public async Task<SyncJoinResult> ExecuteAsync(ISyncTransport transport, Guid groupId, char[] passphrase,
        string targetDatabasePath, string? folder, CancellationToken cancellationToken, string? deviceName = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(passphrase);
        SyncGroupFile.Parse(await transport.ReadAsync(SyncLayout.GroupFile(groupId), cancellationToken)
            ?? throw new InvalidOperationException("Sync group not found."));

        var keyVersion = (await transport.ListAsync(SyncLayout.Group(groupId) + "/", cancellationToken))
            .Select(p => SyncLayout.TryParseNumber(p, "key.", ".wrap", out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();
        if (keyVersion == 0) throw new InvalidOperationException("The sync group has no key.");
        var wrap = SyncKeyWrap.Parse(await transport.ReadAsync(SyncLayout.KeyWrap(groupId, keyVersion), cancellationToken)
            ?? throw new InvalidOperationException("The group key is missing."));
        var key = wrap.Unwrap(_cipher, passphrase);

        var generation = await SyncEngine.LatestGenerationAsync(transport, groupId, cancellationToken);
        var settings = new SyncSettings(groupId, Guid.NewGuid(), generation, keyVersion, folder, deviceName);
        await BuildAsync(transport, settings, key, targetDatabasePath, cancellationToken);
        return new SyncJoinResult(settings, key);
    }

    // After a new generation (§5.7): same group, device and key.
    public async Task<SyncSettings> RejoinAsync(ISyncTransport transport, SyncSettings current, byte[] key,
        string targetDatabasePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        var generation = await SyncEngine.LatestGenerationAsync(transport, current.GroupId, cancellationToken);
        var settings = current with { Generation = generation };
        await BuildAsync(transport, settings, key, targetDatabasePath, cancellationToken);
        return settings;
    }

    private async Task BuildAsync(ISyncTransport transport, SyncSettings settings, byte[] key,
        string targetDatabasePath, CancellationToken ct)
    {
        var (header, content) = await PickImageAsync(transport, settings, key, ct);
        var snapshot = SnapshotContent.Parse(content);
        if (snapshot.SnapshotSchema > _snapshots.SchemaVersion)
            throw new NotSupportedException("The sync group was written by a newer version of the app.");

        var vector = header.Vector ?? new Dictionary<Guid, int>();
        await _snapshots.BuildDatabaseAsync(targetDatabasePath, snapshot.Image, settings.Generation, vector, ct);

        // The record goes up at once: the other devices keep the segments
        // after this image until this device has applied them (§5.6).
        // Segments deleted between the image choice and this write make
        // the first run report RebuildRequired.
        await SyncEngine.WriteRecordAsync(transport, _cipher, key, settings, _options, _clock, 0,
            vector.Where(v => v.Key != settings.DeviceId).ToDictionary(v => v.Key, v => v.Value), ct);
    }

    // §5.6: the newest checkpoint that covers every device folder's first
    // remaining segment, or the genesis when nothing was deleted yet.
    private async Task<(SyncFileHeader Header, byte[] Content)> PickImageAsync(
        ISyncTransport transport, SyncSettings settings, byte[] key, CancellationToken ct)
    {
        var first = new Dictionary<Guid, int>();
        foreach (var path in await transport.ListAsync(SyncLayout.OpsFolder(settings.GroupId, settings.Generation), ct))
        {
            if (!SyncLayout.TryParseSegment(path, out var device, out var seq)) continue;
            first[device] = first.TryGetValue(device, out var known) ? Math.Min(known, seq) : seq;
        }

        var candidates = new List<(string Path, SyncFileHeader Header)>();
        foreach (var path in await transport.ListAsync(SyncLayout.CheckpointFolder(settings.GroupId, settings.Generation), ct))
        {
            var file = await transport.ReadAsync(path, ct);
            if (file is not null) candidates.Add((path, SyncFileCodec.ReadHeader(file)));
        }
        candidates.Add((SyncLayout.Genesis(settings.GroupId, settings.Generation),
            new SyncFileHeader(SyncFileKind.Genesis, 1, settings.GroupId, settings.Generation, Guid.Empty, 0, 0, 0,
                new Dictionary<Guid, int>())));

        foreach (var (path, header) in candidates.OrderByDescending(c => c.Header.Vector?.Values.Sum() ?? 0))
        {
            var vector = header.Vector ?? new Dictionary<Guid, int>();
            if (!first.All(f => vector.GetValueOrDefault(f.Key) >= f.Value - 1)) continue;
            var file = await transport.ReadAsync(path, ct)
                ?? throw new InvalidOperationException("The sync image disappeared; try again.");
            return SyncFileCodec.Open(_cipher, key, file);
        }
        throw new InvalidOperationException("No image of the sync group can be used yet; try again later.");
    }
}

// Starts a new generation (§5.7) after an import or a restore replaced the
// profile database on a synced device: genesis versions and a new genesis
// image. The caller publishes the pending operations of the old
// generation (SyncEngine.RunAsync) before replacing the database. The
// other devices find the new genesis on their next run and rebuild.
public sealed class ResetSyncGeneration
{
    private readonly SyncGenesis _genesis;
    private readonly ISyncSnapshotStore _snapshots;
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncKeyStore _keys;
    private readonly ISyncPeerRepository _peers;
    private readonly IArchiveCipher _cipher;
    private readonly IUnitOfWork _uow;

    private readonly TimeProvider _clock;
    private readonly SyncEngineOptions _options;

    public ResetSyncGeneration(
        SyncGenesis genesis,
        ISyncSnapshotStore snapshots,
        ISyncSettingsStore settings,
        ISyncKeyStore keys,
        ISyncPeerRepository peers,
        IArchiveCipher cipher,
        IUnitOfWork uow,
        TimeProvider clock,
        SyncEngineOptions? options = null)
    {
        _clock = clock;
        _options = options ?? new SyncEngineOptions();
        _genesis = genesis;
        _snapshots = snapshots;
        _settings = settings;
        _keys = keys;
        _peers = peers;
        _cipher = cipher;
        _uow = uow;
    }

    public async Task<SyncSettings> ExecuteAsync(ISyncTransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var current = _settings.Load() ?? throw new InvalidOperationException("Sync is not enabled for this profile.");
        var key = _keys.Load(current.GroupId, current.KeyVersion)
            ?? throw new InvalidOperationException("The group key is not stored on this device.");

        await _genesis.RecordAsync(cancellationToken);
        try
        {
            return await WriteGate.RunExclusiveAsync(async ct =>
            {
                var generation = await SyncEngine.LatestGenerationAsync(transport, current.GroupId, ct) + 1;
                var settings = current with { Generation = generation, ResetPending = false };
                await CreateSyncGroup.WriteGenesisAsync(transport, _cipher, _snapshots, settings, key, ct);
                await SyncEngine.WriteRecordAsync(transport, _cipher, key, settings, _options, _clock, 0,
                    new Dictionary<Guid, int>(), ct);
                await _peers.ClearAsync(ct);
                await _peers.AddAsync(new SyncPeer { DeviceId = settings.DeviceId, Generation = generation }, ct);
                await _uow.SaveChangesAsync(ct);
                _settings.Save(settings);
                return settings;
            }, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
