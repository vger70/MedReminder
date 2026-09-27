using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Application.Sync.Remote;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

public sealed record SyncEngineOptions
{
    public string DeviceName { get; init; } = "device";

    public string Platform { get; init; } = "desktop";

    public string AppVersion { get; init; } = string.Empty;

    // §5.6: a checkpoint after this many operations since the last one.
    public int CheckpointEvery { get; init; } = 5000;

    // §5.6: a device not seen for this long no longer holds segments back.
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromDays(90);
}

public sealed record SyncRunResult(
    int OperationsPublished,
    int SegmentsApplied,
    int OperationsApplied,
    bool CheckpointWritten,
    int SegmentsDeleted,
    // Set when a newer generation exists: the profile must be rebuilt
    // from its genesis (JoinSyncGroup.RejoinAsync). Pending operations of
    // the old generation were published first, for audit only (§5.7).
    int? NewerGeneration,
    // Set when a peer's next segment is gone and a checkpoint covers it:
    // this device was away too long and must rebuild (§5.6).
    bool RebuildRequired,
    // Device-level problems (ids and counters only, never content).
    IReadOnlyList<string> Problems);

// One sync run (B.1 Phase 3c, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §5.3, §5.6; docs/SYNC-FORMAT.md):
//
//   1. publish this device's pending operations as one new segment;
//   2. stop if a newer generation exists;
//   3. apply the other devices' segments in causal order: a segment is
//      applied only when it is the next of its device and this device's
//      applied vector covers its dependency vector (causal buffer);
//   4. write this device's record (applied vector, last seen);
//   5. write a checkpoint when enough operations accumulated;
//   6. delete this device's own segments that a checkpoint covers and
//      every active device has applied.
//
// Each file has one writer (R5); only this device's own files are
// written or deleted. Unreadable files are skipped and reported, and
// retried on the next run.
public sealed class SyncEngine
{
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncKeyStore _keys;
    private readonly ISyncTransport _transport;
    private readonly IArchiveCipher _cipher;
    private readonly ISyncOperationRepository _operations;
    private readonly ISyncPeerRepository _peers;
    private readonly ISyncSnapshotStore _snapshots;
    private readonly ApplyRemoteOperations _apply;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly SyncEngineOptions _options;

    public SyncEngine(
        ISyncSettingsStore settings,
        ISyncKeyStore keys,
        ISyncTransport transport,
        IArchiveCipher cipher,
        ISyncOperationRepository operations,
        ISyncPeerRepository peers,
        ISyncSnapshotStore snapshots,
        ApplyRemoteOperations apply,
        IUnitOfWork uow,
        TimeProvider clock,
        SyncEngineOptions? options = null)
    {
        _settings = settings;
        _keys = keys;
        _transport = transport;
        _cipher = cipher;
        _operations = operations;
        _peers = peers;
        _snapshots = snapshots;
        _apply = apply;
        _uow = uow;
        _clock = clock;
        _options = options ?? new SyncEngineOptions();
    }

    public async Task<SyncRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.Load()
            ?? throw new InvalidOperationException("Sync is not enabled for this profile.");
        var key = _keys.Load(settings.GroupId, settings.KeyVersion)
            ?? throw new InvalidOperationException("The group key is not stored on this device.");
        try
        {
            return await RunCoreAsync(settings, key, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private async Task<SyncRunResult> RunCoreAsync(SyncSettings settings, byte[] key, CancellationToken ct)
    {
        var problems = new List<string>();
        var me = settings.DeviceId;
        var g = settings.Generation;

        var published = await WriteGate.RunExclusiveAsync(c => PublishAsync(settings, key, c), ct);

        var latest = await LatestGenerationAsync(_transport, settings.GroupId, ct);
        if (latest > g)
        {
            return new SyncRunResult(published, 0, 0, false, 0, latest, false, problems);
        }

        var (segmentsApplied, operationsApplied, rebuild) = await PullAsync(settings, key, problems, ct);

        var peers = await PeersAsync(g, ct);
        var self = peers.GetValueOrDefault(me);
        await WriteRecordAsync(_transport, _cipher, key, settings, _options, _clock, self?.Seq ?? 0,
            Vector(peers, me), ct);

        var checkpoint = await WriteGate.RunExclusiveAsync(c => CheckpointAsync(settings, key, c), ct);
        var deleted = await CompactAsync(settings, key, problems, ct);

        return new SyncRunResult(published, segmentsApplied, operationsApplied, checkpoint, deleted, null, rebuild, problems);
    }

    private async Task<int> PublishAsync(SyncSettings settings, byte[] key, CancellationToken ct)
    {
        var me = settings.DeviceId;
        var g = settings.Generation;
        var pending = await _operations.ListPendingAsync(me, g, ct);
        if (pending.Count == 0) return 0;

        var peers = await PeersAsync(g, ct);
        var self = await SelfAsync(peers, me, g, ct);

        var own = (await _transport.ListAsync($"{SyncLayout.OpsFolder(settings.GroupId, g)}{me:N}/", ct))
            .Select(p => SyncLayout.TryParseSegment(p, out _, out var s) ? s : 0)
            .DefaultIfEmpty(0).Max();
        var seq = Math.Max(self.Seq, own) + 1;
        var content = new SegmentContent(Vector(peers, me), [.. pending.Select(SegmentOperation.From)]).ToBytes();

        // A segment written by an interrupted earlier run takes the name:
        // the next number is used and the operations are sent again,
        // which receivers skip by id.
        while (!await _transport.CreateAsync(
                   SyncLayout.Segment(settings.GroupId, g, me, seq),
                   SyncFileCodec.Seal(_cipher, key,
                       Header(SyncFileKind.Segment, settings, me, seq, OperationCodec.CurrentSchemaVersion), content),
                   ct))
        {
            seq++;
        }

        await _operations.MarkPublishedAsync(pending, seq, ct);
        self.Seq = seq;
        await _peers.UpdateAsync(self, ct);
        await _uow.SaveChangesAsync(ct);
        return pending.Count;
    }

    private async Task<(int Segments, int Operations, bool Rebuild)> PullAsync(
        SyncSettings settings, byte[] key, List<string> problems, CancellationToken ct)
    {
        var me = settings.DeviceId;
        var g = settings.Generation;
        var available = new Dictionary<Guid, SortedSet<int>>();
        foreach (var path in await _transport.ListAsync(SyncLayout.OpsFolder(settings.GroupId, g), ct))
        {
            if (!SyncLayout.TryParseSegment(path, out var device, out var seq) || device == me) continue;
            if (!available.TryGetValue(device, out var set)) available[device] = set = new SortedSet<int>();
            set.Add(seq);
        }

        var peers = await PeersAsync(g, ct);
        var ownSeq = peers.GetValueOrDefault(me)?.Seq ?? 0;
        var applied = Vector(peers, me).ToDictionary(p => p.Key, p => p.Value);
        var halted = new HashSet<Guid>();
        var cache = new Dictionary<(Guid, int), SegmentContent>();
        var segments = 0;
        var operations = 0;
        var rebuild = false;

        bool progress;
        do
        {
            progress = false;
            foreach (var (device, seqs) in available)
            {
                if (halted.Contains(device)) continue;
                var next = applied.GetValueOrDefault(device) + 1;
                if (!seqs.Contains(next))
                {
                    if (seqs.Count > 0 && seqs.Min > next)
                    {
                        // §6.3: a gap. Deleted after a checkpoint means this
                        // device must rebuild; otherwise wait and report.
                        rebuild |= await CoveredByCheckpointAsync(settings, device, next, ct);
                        problems.Add($"Segment {next} of device {device:N} is missing.");
                        halted.Add(device);
                    }
                    continue;
                }

                if (!cache.TryGetValue((device, next), out var content))
                {
                    var file = await _transport.ReadAsync(SyncLayout.Segment(settings.GroupId, g, device, next), ct);
                    if (file is null) continue;
                    try
                    {
                        var (header, bytes) = SyncFileCodec.Open(_cipher, key, file);
                        if (header.Kind != SyncFileKind.Segment || header.GroupId != settings.GroupId
                            || header.Generation != g || header.DeviceId != device || header.Seq != next)
                        {
                            throw new InvalidDataException("Segment header does not match its path.");
                        }
                        content = SegmentContent.Parse(bytes);
                    }
                    catch (Exception ex) when (ex is CryptographicException or InvalidDataException
                                                   or NotSupportedException or System.Text.Json.JsonException)
                    {
                        problems.Add($"Segment {next} of device {device:N} cannot be read ({ex.GetType().Name}).");
                        halted.Add(device);
                        continue;
                    }
                    cache[(device, next)] = content;
                }

                // Causal buffer: wait until every dependency is applied.
                var ready = content.Dependencies.All(d =>
                    d.Key == me ? ownSeq >= d.Value : applied.GetValueOrDefault(d.Key) >= d.Value);
                if (!ready) continue;

                var result = await _apply.ExecuteAsync([.. content.Operations.Select(o => o.ToOperation(g))], ct);
                if (result.Blocked is not null)
                {
                    problems.Add($"Segment {next} of device {device:N} needs a newer app: {result.BlockReason}");
                    halted.Add(device);
                    continue;
                }

                applied[device] = next;
                await WriteGate.RunExclusiveAsync(async c =>
                {
                    var current = await PeersAsync(g, c);
                    if (current.TryGetValue(device, out var peer))
                    {
                        peer.Seq = next;
                        await _peers.UpdateAsync(peer, c);
                    }
                    else
                    {
                        await _peers.AddAsync(new SyncPeer { DeviceId = device, Generation = g, Seq = next }, c);
                    }
                    await _uow.SaveChangesAsync(c);
                    return true;
                }, ct);
                segments++;
                operations += result.Applied;
                progress = true;
            }
        }
        while (progress);

        foreach (var (device, seqs) in available.Where(a => !halted.Contains(a.Key)))
        {
            var next = applied.GetValueOrDefault(device) + 1;
            if (seqs.Contains(next)) problems.Add($"Segment {next} of device {device:N} waits for its dependencies.");
        }
        return (segments, operations, rebuild);
    }

    private async Task<bool> CheckpointAsync(SyncSettings settings, byte[] key, CancellationToken ct)
    {
        var me = settings.DeviceId;
        var g = settings.Generation;
        var total = await _operations.CountAsync(g, ct);
        var peers = await PeersAsync(g, ct);
        var self = await SelfAsync(peers, me, g, ct);
        if (total - self.CheckpointOperations < _options.CheckpointEvery) return false;

        var vector = Vector(peers, me).ToDictionary(p => p.Key, p => p.Value);
        vector[me] = self.Seq;
        var n = self.Checkpoints + 1;
        var image = await _snapshots.CaptureAsync(ct);
        var header = Header(SyncFileKind.Checkpoint, settings, me, n, _snapshots.SchemaVersion) with { Vector = vector };
        await _transport.CreateAsync(
            SyncLayout.Checkpoint(settings.GroupId, g, me, n),
            SyncFileCodec.Seal(_cipher, key, header, new SnapshotContent(_snapshots.SchemaVersion, image).ToBytes()),
            ct);

        // Only the newest checkpoint of this device is kept: its vector
        // covers every earlier one.
        foreach (var path in await _transport.ListAsync(SyncLayout.CheckpointFolder(settings.GroupId, g), ct))
        {
            if (SyncLayout.TryParseCheckpoint(path, out var device, out var k) && device == me && k < n)
                await _transport.DeleteAsync(path, ct);
        }

        self.Checkpoints = n;
        self.CheckpointOperations = total;
        await _peers.UpdateAsync(self, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    // §5.6: an own segment goes when a checkpoint covers it and every
    // active device has applied it.
    private async Task<int> CompactAsync(SyncSettings settings, byte[] key, List<string> problems, CancellationToken ct)
    {
        var me = settings.DeviceId;
        var g = settings.Generation;
        var covered = 0;
        foreach (var path in await _transport.ListAsync(SyncLayout.CheckpointFolder(settings.GroupId, g), ct))
        {
            if (!SyncLayout.TryParseCheckpoint(path, out _, out _)) continue;
            var file = await _transport.ReadAsync(path, ct);
            if (file is null) continue;
            var header = SyncFileCodec.ReadHeader(file);
            if (header.Vector is { } v && v.TryGetValue(me, out var s)) covered = Math.Max(covered, s);
        }
        if (covered == 0) return 0;

        var limit = covered;
        var staleBefore = _clock.GetUtcNow() - _options.StaleAfter;
        foreach (var path in await _transport.ListAsync(SyncLayout.DevicesFolder(settings.GroupId), ct))
        {
            var file = await _transport.ReadAsync(path, ct);
            if (file is null) continue;
            DeviceRecordContent record;
            try
            {
                var (header, content) = SyncFileCodec.Open(_cipher, key, file);
                if (header.Kind != SyncFileKind.Device || header.Generation != g) continue;
                record = DeviceRecordContent.Parse(content);
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidDataException or System.Text.Json.JsonException)
            {
                problems.Add("A device record cannot be read.");
                return 0;
            }
            if (record.DeviceId == me || record.LastSeen < staleBefore) continue;
            limit = Math.Min(limit, record.Applied.GetValueOrDefault(me));
        }

        var deleted = 0;
        foreach (var path in await _transport.ListAsync($"{SyncLayout.OpsFolder(settings.GroupId, g)}{me:N}/", ct))
        {
            if (SyncLayout.TryParseSegment(path, out _, out var seq) && seq <= limit)
            {
                await _transport.DeleteAsync(path, ct);
                deleted++;
            }
        }
        return deleted;
    }

    private async Task<bool> CoveredByCheckpointAsync(SyncSettings settings, Guid device, int seq, CancellationToken ct)
    {
        foreach (var path in await _transport.ListAsync(SyncLayout.CheckpointFolder(settings.GroupId, settings.Generation), ct))
        {
            var file = await _transport.ReadAsync(path, ct);
            if (file is not null && SyncFileCodec.ReadHeader(file).Vector is { } v && v.GetValueOrDefault(device) >= seq)
                return true;
        }
        return false;
    }

    // devices/<deviceId>.mrd. Written as soon as a device takes part
    // (group creation, join, new generation) and after every run: the
    // other devices keep their segments until every recorded device has
    // applied them (§5.6).
    internal static Task WriteRecordAsync(ISyncTransport transport, IArchiveCipher cipher, byte[] key,
        SyncSettings settings, SyncEngineOptions options, TimeProvider clock, int publishedSeq,
        IReadOnlyDictionary<Guid, int> applied, CancellationToken ct)
        => transport.WriteAsync(
            SyncLayout.Device(settings.GroupId, settings.DeviceId),
            SyncFileCodec.Seal(cipher, key, Header(SyncFileKind.Device, settings, settings.DeviceId, 0, 1),
                new DeviceRecordContent(settings.DeviceId, options.DeviceName, options.Platform, options.AppVersion,
                    publishedSeq, applied, clock.GetUtcNow()).ToBytes()),
            ct);

    internal static async Task<int> LatestGenerationAsync(ISyncTransport transport, Guid groupId, CancellationToken ct)
        => (await transport.ListAsync(SyncLayout.GenesisFolder(groupId), ct))
            .Select(p => SyncLayout.TryParseNumber(p, string.Empty, ".mrg", out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();

    internal static SyncFileHeader Header(string kind, SyncSettings settings, Guid device, int seq, int contentVersion)
        => new(kind, SyncFileCodec.FormatVersion, settings.GroupId, settings.Generation, device, seq,
            settings.KeyVersion, contentVersion);

    private async Task<Dictionary<Guid, SyncPeer>> PeersAsync(int generation, CancellationToken ct)
        => (await _peers.ListAllAsync(ct)).Where(p => p.Generation == generation).ToDictionary(p => p.DeviceId);

    private async Task<SyncPeer> SelfAsync(Dictionary<Guid, SyncPeer> peers, Guid me, int generation, CancellationToken ct)
    {
        if (peers.TryGetValue(me, out var self)) return self;
        self = new SyncPeer { DeviceId = me, Generation = generation };
        await _peers.AddAsync(self, ct);
        peers[me] = self;
        return self;
    }

    private static Dictionary<Guid, int> Vector(Dictionary<Guid, SyncPeer> peers, Guid me)
        => peers.Values.Where(p => p.DeviceId != me).ToDictionary(p => p.DeviceId, p => p.Seq);
}
