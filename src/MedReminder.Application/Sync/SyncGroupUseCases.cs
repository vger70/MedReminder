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
        ISyncTransport transport, char[] passphrase, SyncTarget target, CancellationToken cancellationToken,
        Argon2Params? kdf = null, string? deviceName = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(passphrase);
        if (_settings.Load() is not null) throw new InvalidOperationException("Sync is already enabled.");

        await _genesis.RecordAsync(cancellationToken);
        return await WriteGate.RunExclusiveAsync(async ct =>
        {
            var settings = new SyncSettings(Guid.NewGuid(), Guid.NewGuid(), 1, 1, target.Folder, deviceName,
                Provider: target.Provider, AccountId: target.AccountId);
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

// A group a passphrase opens, told apart by its devices: the profile name
// is encrypted in the operations and only known after a join.
public sealed record SyncGroupCandidate(Guid GroupId, IReadOnlyList<SyncGroupDevice> Devices);

public sealed record SyncGroupDevice(string Name, DateTimeOffset LastSeen);

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

    // The groups of the storage that the passphrase opens (one group per
    // profile: a storage shared by several profiles has several, and the
    // same passphrase may open more than one). Each key is used only to
    // read the device records, then zeroed.
    public async Task<IReadOnlyList<SyncGroupCandidate>> FindGroupsAsync(ISyncTransport transport, char[] passphrase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(passphrase);
        var found = new List<SyncGroupCandidate>();
        foreach (var groupId in await ListGroupsAsync(transport, cancellationToken))
        {
            SyncGroupKey group;
            try
            {
                group = await SyncKeys.ObtainAsync(transport, _cipher, groupId, new SyncKeySource.Passphrase(passphrase),
                    _clock, cancellationToken);
            }
            catch (CryptographicException)
            {
                continue;
            }
            try
            {
                var settings = new SyncSettings(groupId, Guid.Empty, group.Generation, group.KeyVersion);
                var devices = await SyncEngine.ReadRecordsAsync(transport, _cipher, group.Key, settings, cancellationToken);
                found.Add(new SyncGroupCandidate(groupId, [.. devices.Select(d => new SyncGroupDevice(d.Name, d.LastSeen))]));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(group.Key);
            }
        }
        return found;
    }

    // Throws CryptographicException for a wrong passphrase.
    public Task<SyncJoinResult> ExecuteAsync(ISyncTransport transport, Guid groupId, char[] passphrase,
        string targetDatabasePath, SyncTarget target, CancellationToken cancellationToken, string? deviceName = null)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        return ExecuteAsync(transport, groupId, new SyncKeySource.Passphrase(passphrase), targetDatabasePath, target,
            cancellationToken, deviceName);
    }

    // Phase 4c: with the passphrase or a pairing code (SyncKeys.ObtainAsync
    // lists what each throws). The device joins the generation sealed with
    // the key it obtained.
    public async Task<SyncJoinResult> ExecuteAsync(ISyncTransport transport, Guid groupId, SyncKeySource source,
        string targetDatabasePath, SyncTarget target, CancellationToken cancellationToken, string? deviceName = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(source);
        SyncGroupFile.Parse(await transport.ReadAsync(SyncLayout.GroupFile(groupId), cancellationToken)
            ?? throw new InvalidOperationException("Sync group not found."));

        var group = await SyncKeys.ObtainAsync(transport, _cipher, groupId, source, _clock, cancellationToken);
        var settings = new SyncSettings(groupId, Guid.NewGuid(), group.Generation, group.KeyVersion, target.Folder, deviceName,
            Provider: target.Provider, AccountId: target.AccountId);
        await BuildOrZeroAsync(transport, settings, group.Key, targetDatabasePath, cancellationToken, newDevice: true);
        return new SyncJoinResult(settings, group.Key);
    }

    // After a new generation (§5.7): same group, device and key. The
    // generation is the newest one sealed with that key.
    public async Task<SyncSettings> RejoinAsync(ISyncTransport transport, SyncSettings current, byte[] key,
        string targetDatabasePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        var generation = await SyncKeys.CurrentGenerationAsync(transport, current.GroupId, current.KeyVersion, cancellationToken);
        if (generation == 0) throw new InvalidOperationException("No generation of the sync group uses the stored key.");
        var settings = current with { Generation = generation };
        await BuildAsync(transport, settings, key, targetDatabasePath, cancellationToken);
        return settings;
    }

    // After a key rotation on another device (Phase 4c, §6.2): the new key
    // from the new passphrase or a pairing code, and a new database built
    // from the generation sealed with it. Same group and device. The
    // caller swaps the database in, saves the returned settings and key,
    // then carries this device's own operations over
    // (ApplyRemoteOperations.ApplyCarriedAsync).
    public async Task<SyncJoinResult> RekeyAsync(ISyncTransport transport, SyncSettings current, SyncKeySource source,
        string targetDatabasePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(source);
        var group = await SyncKeys.ObtainAsync(transport, _cipher, current.GroupId, source, _clock, cancellationToken);
        var settings = current with { Generation = group.Generation, KeyVersion = group.KeyVersion, ResetPending = false };
        await BuildOrZeroAsync(transport, settings, group.Key, targetDatabasePath, cancellationToken);
        return new SyncJoinResult(settings, group.Key);
    }

    private async Task BuildOrZeroAsync(ISyncTransport transport, SyncSettings settings, byte[] key,
        string targetDatabasePath, CancellationToken ct, bool newDevice = false)
    {
        try
        {
            await BuildAsync(transport, settings, key, targetDatabasePath, ct, newDevice);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    // newDevice: a first join, whose record exists only because of this
    // call. If the join fails after the record went up (while waiting, or
    // in the build), the record is removed again, or the other devices would keep their segments for a
    // device that never syncs until it counts as stale (§5.6). A device
    // that rebuilds keeps its record: it was in the group before.
    private async Task BuildAsync(ISyncTransport transport, SyncSettings settings, byte[] key,
        string targetDatabasePath, CancellationToken ct, bool newDevice = false)
    {
        try
        {
            var (header, snapshot) = await PickAndAnnounceAsync(transport, settings, key, ct);
            var vector = header.Vector ?? new Dictionary<Guid, int>();
            await _snapshots.BuildDatabaseAsync(targetDatabasePath, snapshot.Image, settings.Generation, vector, ct);
        }
        catch when (newDevice)
        {
            await RemoveRecordAsync(transport, settings);
            throw;
        }
    }

    // Best effort: the build error is the one reported.
    private static async Task RemoveRecordAsync(ISyncTransport transport, SyncSettings settings)
    {
        try
        {
            await transport.DeleteAsync(SyncLayout.Device(settings.GroupId, settings.DeviceId), CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException
                                       or UnauthorizedAccessException)
        {
            // Left for the stale-device rule (§5.6).
        }
    }

    // The record goes up as soon as the image is chosen: the other devices
    // keep the segments after this image until this device has applied
    // them (§5.6). While a provider's listing lags, that is not enough: a
    // device compacting in those seconds does not list the new record and
    // deletes segments the image does not cover. So the join waits until
    // the provider lists its record, then checks with the listing it just
    // fetched that the image still covers every device's first remaining
    // segment, and picks again if it does not. After JoinAttempts, or when
    // the record is not listed within JoinListingTimeout, the join goes on
    // and the first run reports RebuildRequired as before. An image from a
    // newer app is refused before the record goes up.
    private async Task<(SyncFileHeader Header, SnapshotContent Snapshot)> PickAndAnnounceAsync(
        ISyncTransport transport, SyncSettings settings, byte[] key, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var (header, content) = await PickImageAsync(transport, settings, key, ct);
            var snapshot = SnapshotContent.Parse(content);
            if (snapshot.SnapshotSchema > _snapshots.SchemaVersion)
                throw new NotSupportedException("The sync group was written by a newer version of the app.");

            var vector = header.Vector ?? new Dictionary<Guid, int>();
            await SyncEngine.WriteRecordAsync(transport, _cipher, key, settings, _options, _clock, 0,
                vector.Where(v => v.Key != settings.DeviceId).ToDictionary(v => v.Key, v => v.Value), ct);
            await WaitUntilListedAsync(transport, settings, ct);
            if (attempt >= _options.JoinAttempts
                || Covers(vector, await FirstSegmentsAsync(transport, settings, ct)))
            {
                return (header, snapshot);
            }
        }
    }

    // A provider transport lists its own writes at once, from memory; the
    // other devices see the record only when the provider lists it. A
    // folder transport has no such delay.
    private async Task WaitUntilListedAsync(ISyncTransport transport, SyncSettings settings, CancellationToken ct)
    {
        if (transport is not IProviderListing provider) return;
        var record = SyncLayout.Device(settings.GroupId, settings.DeviceId);
        var polls = Math.Max(1, (int)Math.Ceiling(_options.JoinListingTimeout / _options.JoinListingPoll));
        for (var poll = 0; poll < polls; poll++)
        {
            if (await provider.IsListedByProviderAsync(record, ct)) return;
            await _options.Delay(_options.JoinListingPoll, ct);
        }
    }

    // First remaining segment of each device folder of the generation.
    private static async Task<Dictionary<Guid, int>> FirstSegmentsAsync(ISyncTransport transport,
        SyncSettings settings, CancellationToken ct)
    {
        var first = new Dictionary<Guid, int>();
        foreach (var path in await transport.ListAsync(SyncLayout.OpsFolder(settings.GroupId, settings.Generation), ct))
        {
            if (!SyncLayout.TryParseSegment(path, out var device, out var seq)) continue;
            first[device] = first.TryGetValue(device, out var known) ? Math.Min(known, seq) : seq;
        }
        return first;
    }

    // An image covers the folders when no segment after its vector is gone.
    private static bool Covers(IReadOnlyDictionary<Guid, int> vector, Dictionary<Guid, int> first)
        => first.All(f => vector.GetValueOrDefault(f.Key) >= f.Value - 1);

    // §5.6: the newest checkpoint that covers every device folder's first
    // remaining segment, or the genesis when nothing was deleted yet.
    private async Task<(SyncFileHeader Header, byte[] Content)> PickImageAsync(
        ISyncTransport transport, SyncSettings settings, byte[] key, CancellationToken ct)
    {
        var first = await FirstSegmentsAsync(transport, settings, ct);

        var candidates = new List<(string Path, SyncFileHeader Header)>();
        foreach (var path in await transport.ListAsync(SyncLayout.CheckpointFolder(settings.GroupId, settings.Generation), ct))
        {
            var file = await transport.ReadAsync(path, ct);
            if (file is null) continue;
            SyncFileHeader header;
            try
            {
                header = SyncFileCodec.ReadHeader(file);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
            {
                continue;
            }
            // Phase 4c: a generation has one key; a checkpoint sealed
            // with another one was not written by a device of it.
            if (header.KeyVersion == settings.KeyVersion) candidates.Add((path, header));
        }
        candidates.Add((SyncLayout.Genesis(settings.GroupId, settings.Generation),
            new SyncFileHeader(SyncFileKind.Genesis, 1, settings.GroupId, settings.Generation, Guid.Empty, 0, 0, 0,
                new Dictionary<Guid, int>())));

        foreach (var (path, header) in candidates.OrderByDescending(c => c.Header.Vector?.Values.Sum() ?? 0))
        {
            if (!Covers(header.Vector ?? new Dictionary<Guid, int>(), first)) continue;
            var file = await transport.ReadAsync(path, ct)
                ?? throw new InvalidOperationException("The sync image disappeared; try again.");
            if (header.Kind == SyncFileKind.Genesis) return SyncFileCodec.Open(_cipher, key, file);
            try
            {
                return SyncFileCodec.Open(_cipher, key, file);
            }
            catch (CryptographicException)
            {
                // A damaged or forged checkpoint: an older image will do.
            }
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
