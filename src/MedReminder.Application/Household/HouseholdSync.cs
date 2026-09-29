using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Application.Household.Remote;
using MedReminder.Application.Sync;
using MedReminder.Application.Sync.Remote;
using MedReminder.Domain.Household;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Household;

public sealed record HouseholdSyncResult(
    int OperationsPublished,
    int SegmentsApplied,
    int OperationsApplied,
    // Ids, counters and setting names only.
    IReadOnlyList<string> Problems);

// Replication of the household (household feature, step H3a;
// docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §5): the household
// group on a storage, with the same envelope, key wrap, segments and
// device records as a profile group (docs/SYNC-FORMAT.md) and its own
// operation catalogue. Differences from SyncEngine, and why:
//
//   - the image (genesis) is the whole operation log, and there are no
//     checkpoints and no compaction: a household holds a few profiles and
//     settings that change rarely;
//   - one generation and one key version until device removal (step H5);
//   - a secret setting (the SMTP password) is protected with the local
//     credential protector in the local log, and travels in clear only
//     inside the encrypted segment: it is revealed when a segment is
//     sealed and protected again when one is applied.
//
// After applying remote operations the projection writes the winners into
// profiles.json and the settings files. Callers serialize runs (one
// background service, or a user action).
public sealed class HouseholdSync
{
    private readonly IHouseholdStore _store;
    private readonly IHouseholdKeyStore _keys;
    private readonly ISyncTransportFactory _transports;
    private readonly IArchiveCipher _cipher;
    private readonly ICredentialProtector _protector;
    private readonly HouseholdProjection _projection;
    private readonly TimeProvider _clock;
    private readonly SyncEngineOptions _options;

    public HouseholdSync(IHouseholdStore store, IHouseholdKeyStore keys, ISyncTransportFactory transports,
        IArchiveCipher cipher, ICredentialProtector protector, HouseholdProjection projection, TimeProvider clock,
        SyncEngineOptions? options = null)
    {
        _store = store;
        _keys = keys;
        _transports = transports;
        _cipher = cipher;
        _protector = protector;
        _projection = projection;
        _clock = clock;
        _options = options ?? new SyncEngineOptions();
    }

    // Publishes this installation's household on a storage: household.json,
    // the key wrapped with the household passphrase, the genesis (the whole
    // local log) and this device's record.
    public async Task<HouseholdIdentity> PublishAsync(SyncTarget target, char[] passphrase, string deviceName,
        CancellationToken cancellationToken, Argon2Params? kdf = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(passphrase);
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is not null) throw new InvalidOperationException("The household is already published.");

        var transport = _transports.Create(target);
        var key = RandomNumberGenerator.GetBytes(SyncKeyWrap.KeySize);
        try
        {
            if (!await transport.CreateAsync(HouseholdFile.PathOf(identity.HouseholdId),
                    HouseholdFile.Create(identity.HouseholdId).ToBytes(), cancellationToken))
            {
                throw new InvalidOperationException("The household already exists in this storage.");
            }
            await transport.CreateAsync(SyncLayout.KeyWrap(identity.HouseholdId, 1),
                SyncKeyWrap.Wrap(_cipher, identity.HouseholdId, 1, key, passphrase, kdf ?? Argon2Params.Default).ToBytes(),
                cancellationToken);

            var log = await _store.ListOperationsAsync(cancellationToken);
            var image = new HouseholdSegmentContent(new Dictionary<Guid, int>(), [.. log.Select(Outgoing)]);
            var header = Header(SyncFileKind.Genesis, identity, identity.DeviceId, 0) with { Vector = new Dictionary<Guid, int>() };
            await transport.CreateAsync(SyncLayout.Genesis(identity.HouseholdId, identity.Generation),
                SyncFileCodec.Seal(_cipher, key, header, image.ToBytes()), cancellationToken);
            await _store.MarkPublishedAsync([.. log.Select(o => o.Id)], 0, cancellationToken);

            var published = identity with { KeyVersion = 1, Storage = target, DeviceName = deviceName, SegmentSeq = 0 };
            await WriteRecordAsync(transport, key, published, new Dictionary<Guid, int>(), cancellationToken);
            _keys.Save(published.HouseholdId, 1, key);
            await _store.SaveIdentityAsync(published, cancellationToken);
            return published;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    // Joins the household of a storage with the household passphrase: the
    // local household (log, registers) is replaced by the household's,
    // then a first run brings the segments and projects the winners.
    // Throws CryptographicException for a wrong passphrase.
    public async Task<HouseholdSyncResult> JoinAsync(SyncTarget target, Guid householdId, char[] passphrase,
        string deviceName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(passphrase);
        var transport = _transports.Create(target);
        HouseholdFile.Parse(await transport.ReadAsync(HouseholdFile.PathOf(householdId), cancellationToken)
            ?? throw new InvalidOperationException("Household not found."));

        var obtained = await SyncKeys.ObtainAsync(transport, _cipher, householdId,
            new SyncKeySource.Passphrase(passphrase), _clock, cancellationToken);
        try
        {
            var genesis = await transport.ReadAsync(SyncLayout.Genesis(householdId, obtained.Generation), cancellationToken)
                ?? throw new InvalidOperationException("The household has no genesis.");
            var (_, content) = SyncFileCodec.Open(_cipher, obtained.Key, genesis);
            var image = HouseholdSegmentContent.Parse(content);

            var current = await _store.EnsureCreatedAsync(cancellationToken);
            var identity = new HouseholdIdentity(householdId, current.DeviceId, obtained.Generation, obtained.KeyVersion,
                target, deviceName, 0);
            await HouseholdLog.Gate.WaitAsync(cancellationToken);
            try
            {
                await _store.ResetAsync(identity, cancellationToken);
                foreach (var operation in image.Operations)
                {
                    await ApplyOperationAsync(operation, identity.Generation, cancellationToken);
                }
            }
            finally
            {
                HouseholdLog.Gate.Release();
            }
            await _store.MarkPublishedAsync([.. image.Operations.Select(o => o.Id)], 0, cancellationToken);
            _keys.Save(householdId, obtained.KeyVersion, obtained.Key);
            await WriteRecordAsync(transport, obtained.Key, identity, new Dictionary<Guid, int>(), cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(obtained.Key);
        }
        return await RunAsync(cancellationToken);
    }

    // One run: publish this device's pending operations, apply the other
    // devices' segments in causal order, project, write the device record.
    // Does nothing while the household is not on a storage.
    public async Task<HouseholdSyncResult> RunAsync(CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is null) return new HouseholdSyncResult(0, 0, 0, []);
        var key = _keys.Load(identity.HouseholdId, identity.KeyVersion)
            ?? throw new InvalidOperationException("The household key is not stored on this device.");
        try
        {
            var transport = _transports.Create(identity.Storage);
            var problems = new List<string>();
            var (published, identityAfter) = await PublishPendingAsync(transport, key, identity, cancellationToken);
            var (segments, operations) = await PullAsync(transport, key, identityAfter, problems, cancellationToken);
            problems.AddRange(await _projection.ProjectAsync(cancellationToken));
            await WriteRecordAsync(transport, key, identityAfter, await _store.GetAppliedAsync(cancellationToken),
                cancellationToken);
            return new HouseholdSyncResult(published, segments, operations, problems);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private async Task<(int Published, HouseholdIdentity Identity)> PublishPendingAsync(ISyncTransport transport,
        byte[] key, HouseholdIdentity identity, CancellationToken ct)
    {
        var pending = await _store.ListPendingAsync(identity.DeviceId, ct);
        if (pending.Count == 0) return (0, identity);

        var own = (await transport.ListAsync(
                $"{SyncLayout.OpsFolder(identity.HouseholdId, identity.Generation)}{identity.DeviceId:N}/", ct))
            .Select(p => SyncLayout.TryParseSegment(p, out _, out var s) ? s : 0)
            .DefaultIfEmpty(0).Max();
        var seq = Math.Max(identity.SegmentSeq, own) + 1;
        var content = new HouseholdSegmentContent(await _store.GetAppliedAsync(ct),
            [.. pending.OrderBy(o => o.Timestamp).Select(Outgoing)]).ToBytes();

        // A segment left by an interrupted run takes the name: the next
        // number is used, and receivers skip operations they have by id.
        while (!await transport.CreateAsync(
                   SyncLayout.Segment(identity.HouseholdId, identity.Generation, identity.DeviceId, seq),
                   SyncFileCodec.Seal(_cipher, key, Header(SyncFileKind.Segment, identity, identity.DeviceId, seq), content),
                   ct))
        {
            seq++;
        }
        await _store.MarkPublishedAsync([.. pending.Select(o => o.Id)], seq, ct);
        var updated = identity with { SegmentSeq = seq };
        await _store.SaveIdentityAsync(updated, ct);
        return (pending.Count, updated);
    }

    private async Task<(int Segments, int Operations)> PullAsync(ISyncTransport transport, byte[] key,
        HouseholdIdentity identity, List<string> problems, CancellationToken ct)
    {
        var me = identity.DeviceId;
        var available = new Dictionary<Guid, SortedSet<int>>();
        foreach (var path in await transport.ListAsync(SyncLayout.OpsFolder(identity.HouseholdId, identity.Generation), ct))
        {
            if (!SyncLayout.TryParseSegment(path, out var device, out var seq) || device == me) continue;
            if (!available.TryGetValue(device, out var set)) available[device] = set = new SortedSet<int>();
            set.Add(seq);
        }

        var applied = new Dictionary<Guid, int>(await _store.GetAppliedAsync(ct));
        var halted = new HashSet<Guid>();
        var cache = new Dictionary<(Guid, int), HouseholdSegmentContent>();
        var segments = 0;
        var operations = 0;
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
                        // No compaction deletes household segments: a gap is
                        // a segment the storage has not synced yet.
                        problems.Add($"Household segment {next} of device {device:N} is missing.");
                        halted.Add(device);
                    }
                    continue;
                }

                if (!cache.TryGetValue((device, next), out var content))
                {
                    var file = await transport.ReadAsync(SyncLayout.Segment(identity.HouseholdId, identity.Generation, device, next), ct);
                    if (file is null) continue;
                    try
                    {
                        var (header, bytes) = SyncFileCodec.Open(_cipher, key, file);
                        if (header.Kind != SyncFileKind.Segment || header.GroupId != identity.HouseholdId
                            || header.Generation != identity.Generation || header.DeviceId != device || header.Seq != next)
                        {
                            throw new InvalidDataException("Household segment header does not match its path.");
                        }
                        content = HouseholdSegmentContent.Parse(bytes);
                    }
                    catch (Exception ex) when (ex is CryptographicException or InvalidDataException
                                                   or NotSupportedException or System.Text.Json.JsonException)
                    {
                        problems.Add($"Household segment {next} of device {device:N} cannot be read ({ex.GetType().Name}).");
                        halted.Add(device);
                        continue;
                    }
                    cache[(device, next)] = content;
                }

                // Causal buffer, as in a profile group.
                if (!content.Dependencies.All(d =>
                        d.Key == me ? identity.SegmentSeq >= d.Value : applied.GetValueOrDefault(d.Key) >= d.Value))
                {
                    continue;
                }

                await HouseholdLog.Gate.WaitAsync(ct);
                try
                {
                    foreach (var operation in content.Operations)
                    {
                        if (await ApplyOperationAsync(operation, identity.Generation, ct)) operations++;
                    }
                }
                catch (NotSupportedException ex)
                {
                    problems.Add($"Household segment {next} of device {device:N} needs a newer app: {ex.Message}");
                    halted.Add(device);
                    continue;
                }
                finally
                {
                    HouseholdLog.Gate.Release();
                }
                applied[device] = next;
                await _store.SetAppliedAsync(device, next, ct);
                segments++;
                progress = true;
            }
        }
        while (progress);
        return (segments, operations);
    }

    // Adds a remote operation to the local log with its register versions;
    // false when it is already there. NotSupportedException for a type or
    // schema this app does not know.
    private async Task<bool> ApplyOperationAsync(HouseholdSegmentOperation operation, int generation, CancellationToken ct)
    {
        if (await _store.ExistsAsync(operation.Id, ct)) return false;
        var body = HouseholdOperationCodec.Deserialize(operation.Type, operation.SchemaVersion, operation.Payload);
        if (body is HouseholdSettingChanged { Value: { } clear } secret && HouseholdSetting.IsSecret(secret.Setting))
        {
            body = secret with { Value = _protector.Protect(clear) };
        }
        var (_, payload) = HouseholdOperationCodec.Serialize(body);
        var timestamp = new HybridTimestamp(operation.PhysicalMs, operation.Counter, operation.DeviceId);
        await _store.AppendAsync(
            new HouseholdOperationRecord(operation.Id, timestamp, generation, operation.Type, operation.SchemaVersion,
                operation.ProfileId, payload),
            [.. HouseholdRegisters.WritesOf(body).Select(w => new HouseholdRegisterVersion(body.ProfileId, w.Register, timestamp, w.Value))],
            ct);
        return true;
    }

    // The wire form of a local operation: a secret setting in clear, for
    // the encrypted segment only.
    private HouseholdSegmentOperation Outgoing(HouseholdOperationRecord record)
    {
        var payload = record.Payload;
        var body = HouseholdOperationCodec.Deserialize(record.Type, record.SchemaVersion, record.Payload);
        if (body is HouseholdSettingChanged { Value: { } protectedValue } secret && HouseholdSetting.IsSecret(secret.Setting))
        {
            (_, payload) = HouseholdOperationCodec.Serialize(secret with { Value = _protector.Unprotect(protectedValue) });
        }
        return new HouseholdSegmentOperation(record.Id, record.Timestamp.PhysicalMs, record.Timestamp.Counter,
            record.Timestamp.DeviceId, record.Type, record.SchemaVersion, record.ProfileId, payload);
    }

    private Task WriteRecordAsync(ISyncTransport transport, byte[] key, HouseholdIdentity identity,
        IReadOnlyDictionary<Guid, int> applied, CancellationToken ct)
        => SyncEngine.WriteRecordAsync(transport, _cipher, key, AsGroup(identity), _options, _clock,
            identity.SegmentSeq, applied, ct);

    private static SyncFileHeader Header(string kind, HouseholdIdentity identity, Guid device, int seq)
        => new(kind, SyncFileCodec.FormatVersion, identity.HouseholdId, identity.Generation, device, seq,
            identity.KeyVersion, HouseholdSegmentContent.ContentVersion);

    // The household seen as a group, for the helpers shared with profile groups.
    private static SyncSettings AsGroup(HouseholdIdentity identity)
        => new(identity.HouseholdId, identity.DeviceId, identity.Generation, identity.KeyVersion,
            identity.Storage?.Folder, identity.DeviceName, Provider: identity.Storage?.Provider,
            AccountId: identity.Storage?.AccountId);
}
