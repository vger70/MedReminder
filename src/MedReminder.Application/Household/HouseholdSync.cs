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
    IReadOnlyList<string> Problems,
    // Step H5a: the household key was changed (a device was removed); this
    // device publishes nothing until it has the new key (RekeyAsync).
    bool NewKeyRequired = false,
    // Step H5a: this device was removed from the household.
    bool Removed = false);

// Step H5a: how a device that needs the new household key obtains it.
public abstract record HouseholdKeySource
{
    private HouseholdKeySource()
    {
    }

    // The caller zeroes the array after use.
    public sealed record Passphrase(char[] Value) : HouseholdKeySource;

    public sealed record Code(HouseholdPairingCode Value) : HouseholdKeySource;
}

// Step H5a: this device was removed from the household; it cannot take
// the new key.
public sealed class HouseholdDeviceRemovedException : InvalidOperationException
{
    public HouseholdDeviceRemovedException()
        : base("This device was removed from the household.")
    {
    }
}

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
    private readonly HouseholdKeyring _keyring;
    private readonly HouseholdLog _household;
    private readonly TimeProvider _clock;
    private readonly SyncEngineOptions _options;

    public HouseholdSync(IHouseholdStore store, IHouseholdKeyStore keys, ISyncTransportFactory transports,
        IArchiveCipher cipher, ICredentialProtector protector, HouseholdProjection projection, HouseholdKeyring keyring,
        HouseholdLog household, TimeProvider clock, SyncEngineOptions? options = null)
    {
        _keyring = keyring;
        _household = household;
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

            // Step H3b: this device's key, the recovery key and the
            // existing profile groups go into the genesis.
            await _keyring.EnsureDeviceKeyAsync(cancellationToken);
            var recovery = Convert.FromBase64String(await _keyring.CreateRecoveryKeyAsync(1, cancellationToken));
            try
            {
                await transport.CreateAsync(RecoveryWrapPath(identity.HouseholdId, 1),
                    SyncKeyWrap.Wrap(_cipher, identity.HouseholdId, 1, recovery, passphrase, kdf ?? Argon2Params.Default,
                        RecoveryPurpose).ToBytes(), cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(recovery);
            }
            await _keyring.AdoptProfileGroupsAsync(cancellationToken);
            // Step H4a (R1): the device that publishes is the master, unless
            // the household already has one.
            if ((await _household.MasterAsync(cancellationToken)).Election is null)
            {
                var electionId = Guid.NewGuid();
                await _household.AppendAsync(
                [
                    new MasterElected(electionId, identity.DeviceId, string.Empty, MasterElectionKind.Creation),
                    new MasterActivated(electionId, identity.DeviceId),
                ], cancellationToken);
            }

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
        await JoinCoreAsync(transport, target, householdId, obtained, deviceName, cancellationToken);
        return await RunAsync(cancellationToken);
    }

    // Step H3c: joins with a household pairing code (mrpair2). The offer
    // holds the household key and the group keys of the profiles an admin
    // selected; this device records their grants to itself, so it keeps
    // them after the offer ends. Returns the granted profile ids.
    // SyncPairingExpiredException when the offer is over,
    // CryptographicException when a newer offer replaced it.
    public async Task<(HouseholdSyncResult Result, IReadOnlyList<string> Granted)> JoinAsync(SyncTarget target,
        HouseholdPairingCode code, string deviceName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(code);
        var transport = _transports.Create(target);
        HouseholdFile.Parse(await transport.ReadAsync(HouseholdFile.PathOf(code.HouseholdId), cancellationToken)
            ?? throw new InvalidOperationException("Household not found."));
        var file = await transport.ReadAsync(SyncLayout.Pairing(code.HouseholdId, code.DeviceId), cancellationToken)
            ?? throw new SyncPairingExpiredException();
        var offer = HouseholdPairingFile.Parse(file).Open(_cipher, code, _clock.GetUtcNow());
        try
        {
            var obtained = await SyncKeys.ObtainAsync(transport, _cipher, code.HouseholdId,
                new SyncKeySource.Known(offer.KeyVersion, offer.Key), _clock, cancellationToken);
            await JoinCoreAsync(transport, target, code.HouseholdId, obtained, deviceName, cancellationToken);
            foreach (var profile in offer.Profiles)
            {
                await _keyring.AcceptAsync(profile.ProfileId,
                    new ProfileGroupKey(profile.GroupId, profile.KeyVersion, profile.Key), cancellationToken);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(offer.Key);
            foreach (var profile in offer.Profiles) CryptographicOperations.ZeroMemory(profile.Key);
        }
        return (await RunAsync(cancellationToken), [.. offer.Profiles.Select(p => p.ProfileId)]);
    }

    // Step H3c, join with the household passphrase (§6.3 page 5): after an
    // admin approved on this device (JoinInstallation), the passphrase
    // opens the recovery key and the escrows of the selected profiles are
    // granted to this device. Returns the profile ids granted; a profile
    // without escrow is left out. CryptographicException for a wrong
    // passphrase.
    public async Task<IReadOnlyList<string>> GrantFromEscrowAsync(IReadOnlyCollection<string> profileIds,
        char[] passphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileIds);
        ArgumentNullException.ThrowIfNull(passphrase);
        var granted = new List<string>();
        if (profileIds.Count == 0) return granted;
        var recovery = await OpenRecoveryKeyAsync(passphrase, cancellationToken);
        if (recovery is null) return granted;
        foreach (var profileId in profileIds.Distinct(StringComparer.Ordinal))
        {
            var group = await _keyring.OpenEscrowAsync(profileId, recovery, cancellationToken);
            if (group is null) continue;
            try
            {
                await _keyring.AcceptAsync(profileId, group, cancellationToken);
                granted.Add(profileId);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(group.Key);
            }
        }
        await RunAsync(cancellationToken);
        return granted;
    }

    // The household replaces the local one; zeroes the key.
    private async Task JoinCoreAsync(ISyncTransport transport, SyncTarget target, Guid householdId, SyncGroupKey obtained,
        string deviceName, CancellationToken cancellationToken, IReadOnlyList<HouseholdSegmentOperation>? carried = null)
    {
        try
        {
            var genesis = await transport.ReadAsync(SyncLayout.Genesis(householdId, obtained.Generation), cancellationToken)
                ?? throw new InvalidOperationException("The household has no genesis.");
            var (_, content) = SyncFileCodec.Open(_cipher, obtained.Key, genesis);
            var image = HouseholdSegmentContent.Parse(content);

            var current = await _store.EnsureCreatedAsync(cancellationToken);
            // Step H5a: a removed device takes nothing; its local household
            // stays as it was.
            if (image.Operations.Any(o => o.Type == nameof(DeviceRemoved)
                    && HouseholdOperationCodec.Deserialize(o.Type, o.SchemaVersion, o.Payload) is DeviceRemoved r
                    && r.DeviceId == current.DeviceId))
            {
                throw new HouseholdDeviceRemovedException();
            }
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
            // Step H5a: this device's own operations the new genesis lacks
            // stay pending and go out with the next run.
            if (carried is { Count: > 0 })
            {
                await HouseholdLog.Gate.WaitAsync(cancellationToken);
                try
                {
                    foreach (var operation in carried) await ApplyOperationAsync(operation, identity.Generation, cancellationToken);
                }
                finally
                {
                    HouseholdLog.Gate.Release();
                }
            }
            _keys.Save(householdId, obtained.KeyVersion, obtained.Key);
            // Step H3b: this device's public key goes up with the first run.
            await _keyring.EnsureDeviceKeyAsync(cancellationToken);
            await WriteRecordAsync(transport, obtained.Key, identity, new Dictionary<Guid, int>(), cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(obtained.Key);
        }
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
            // Step H5a: after a removal the household runs on a new key and
            // generation; this device stops until it has them.
            if ((await SyncKeys.KeyVersionsAsync(transport, identity.HouseholdId, cancellationToken)).FirstOrDefault()
                > identity.KeyVersion)
            {
                var removed = (await _household.KeysAsync(cancellationToken)).IsRemoved(identity.DeviceId);
                return new HouseholdSyncResult(0, 0, 0, [], NewKeyRequired: true, Removed: removed);
            }
            // Step H3b: a profile synced since the last run joins the household.
            await _keyring.EnsureDeviceKeyAsync(cancellationToken);
            await _keyring.AdoptProfileGroupsAsync(cancellationToken);
            var (published, identityAfter) = await PublishPendingAsync(transport, key, identity, cancellationToken);
            var (segments, operations) = await PullAsync(transport, key, identityAfter, problems, cancellationToken);
            // Step H4a: the lease counts from here; then the master
            // bookkeeping, published in the same run.
            identityAfter = identityAfter with { LastSyncedAt = _clock.GetUtcNow() };
            await _store.SaveIdentityAsync(identityAfter, cancellationToken);
            if (await UpdateMasterAsync(transport, key, identityAfter, cancellationToken))
            {
                var (more, afterMaster) = await PublishPendingAsync(transport, key, identityAfter, cancellationToken);
                published += more;
                identityAfter = afterMaster;
            }
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

    // Step H3b: the group key of a profile from its escrow, when no device
    // that holds it is at hand. The household passphrase opens the recovery
    // private key (recovery.<v>.wrap); CryptographicException for a wrong
    // passphrase. Null when the household holds no escrow for the profile.
    public async Task<ProfileGroupKey?> RecoverProfileKeyAsync(string profileId, char[] passphrase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        var recovery = await OpenRecoveryKeyAsync(passphrase, cancellationToken);
        return recovery is null ? null : await _keyring.OpenEscrowAsync(profileId, recovery, cancellationToken);
    }

    // The recovery private key (PKCS#8, base64); null when the household
    // has no recovery key. Argon2id runs once per call.
    private async Task<string?> OpenRecoveryKeyAsync(char[] passphrase, CancellationToken ct)
    {
        var identity = await _store.EnsureCreatedAsync(ct);
        if (identity.Storage is null) throw new InvalidOperationException("The household is not published.");
        var keys = await _keyring.KeysAsync(ct);
        if (keys.RecoveryPublicKey is not { } recovery) return null;
        var file = await _transports.Create(identity.Storage)
            .ReadAsync(RecoveryWrapPath(identity.HouseholdId, recovery.KeyVersion), ct)
            ?? throw new InvalidOperationException("The recovery key is missing from the storage.");
        var privateKey = SyncKeyWrap.Parse(file).Unwrap(_cipher, passphrase, RecoveryPurpose);
        try
        {
            return Convert.ToBase64String(privateKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    // Step H5a (docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §9, D-15
    // option A): removes a device. The removed device holds the household
    // key, so the household moves to a new key and a new generation:
    //
    //   1. a run, so the new genesis holds the latest changes;
    //   2. DeviceRemoved, the revocation of its grants, a new recovery key
    //      pair and the escrows of the profiles this device holds for it;
    //      when the removed device was the master or the elected one, this
    //      device takes over (or the outgoing master is elected again);
    //   3. key.<v+1>.wrap and recovery.<v+1>.wrap with the new household
    //      passphrase, then the genesis of generation g+1 (the whole log)
    //      sealed with the new key, then this device's record;
    //   4. the local household moves to the new generation.
    //
    // The other devices find a newer key, stop publishing and ask for the
    // new passphrase or a code (RekeyAsync). CryptographicException never:
    // the passphrase is new. InvalidOperationException when this device is
    // behind (it needs the new key itself), the device is unknown or
    // already removed, or another device is changing the key.
    public async Task RemoveDeviceAsync(Guid deviceId, char[] passphrase, string electedBy,
        CancellationToken cancellationToken, Argon2Params? kdf = null)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        var before = await RunAsync(cancellationToken);
        if (before.NewKeyRequired) throw new InvalidOperationException("This device needs the new household key first.");
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is null) throw new InvalidOperationException("The household is not published.");
        if (deviceId == identity.DeviceId) throw new InvalidOperationException("A device cannot remove itself.");
        var transport = _transports.Create(identity.Storage);
        var keys = await _household.KeysAsync(cancellationToken);
        if (keys.IsRemoved(deviceId)) throw new InvalidOperationException("The device is already removed.");
        if (!keys.DevicePublicKeys.ContainsKey(deviceId)
            && (await ListDevicesAsync(cancellationToken)).All(r => r.DeviceId != deviceId))
        {
            throw new InvalidOperationException("The device is not part of the household.");
        }

        var version = (await SyncKeys.KeyVersionsAsync(transport, identity.HouseholdId, cancellationToken))
            .DefaultIfEmpty(identity.KeyVersion).Max() + 1;
        var (recoveryPrivate, recoveryPublic) = HouseholdKeyWrap.CreateKeyPair();
        var operations = new List<HouseholdOperationBody> { new DeviceRemoved(deviceId) };
        operations.AddRange(keys.Grants.Keys.Where(g => g.DeviceId == deviceId)
            .Select(g => new ProfileKeyRevoked(g.ProfileId, deviceId)));
        operations.Add(new RecoveryKeyPublished(version, recoveryPublic));
        operations.AddRange(await _keyring.EscrowAllAsync(recoveryPublic, cancellationToken));
        operations.AddRange(MasterAfterRemoval(await _household.MasterAsync(cancellationToken), deviceId,
            identity.DeviceId, electedBy));
        await _household.AppendAsync(operations, cancellationToken);

        var key = RandomNumberGenerator.GetBytes(SyncKeyWrap.KeySize);
        var recovery = Convert.FromBase64String(recoveryPrivate);
        try
        {
            // Wraps first: an interruption leaves a wrap without a
            // generation, which no device takes (SyncKeys.ObtainAsync).
            if (!await transport.CreateAsync(SyncLayout.KeyWrap(identity.HouseholdId, version),
                    SyncKeyWrap.Wrap(_cipher, identity.HouseholdId, version, key, passphrase, kdf ?? Argon2Params.Default)
                        .ToBytes(), cancellationToken))
            {
                throw new InvalidOperationException("The household key is being changed on another device.");
            }
            await transport.CreateAsync(RecoveryWrapPath(identity.HouseholdId, version),
                SyncKeyWrap.Wrap(_cipher, identity.HouseholdId, version, recovery, passphrase, kdf ?? Argon2Params.Default,
                    RecoveryPurpose).ToBytes(), cancellationToken);

            var generation = await SyncEngine.LatestGenerationAsync(transport, identity.HouseholdId, cancellationToken) + 1;
            var moved = identity with { KeyVersion = version, Generation = generation, SegmentSeq = 0 };
            var image = new HouseholdSegmentContent(new Dictionary<Guid, int>(),
                [.. (await _store.ListOperationsAsync(cancellationToken)).Select(Outgoing)]);
            var header = Header(SyncFileKind.Genesis, moved, moved.DeviceId, 0) with { Vector = new Dictionary<Guid, int>() };
            await transport.CreateAsync(SyncLayout.Genesis(identity.HouseholdId, generation),
                SyncFileCodec.Seal(_cipher, key, header, image.ToBytes()), cancellationToken);

            await HouseholdLog.Gate.WaitAsync(cancellationToken);
            try
            {
                await _store.ResetAsync(moved, cancellationToken);
                foreach (var operation in image.Operations) await ApplyOperationAsync(operation, generation, cancellationToken);
            }
            finally
            {
                HouseholdLog.Gate.Release();
            }
            await _store.MarkPublishedAsync([.. image.Operations.Select(o => o.Id)], 0, cancellationToken);
            _keys.Save(identity.HouseholdId, version, key);
            await WriteRecordAsync(transport, key, moved, new Dictionary<Guid, int>(), cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(recovery);
        }
    }

    // §9 last line: removing the master is a removal plus a takeover. When
    // the removed device was the elected one, the master still active is
    // elected again (it activates without a wizard), or this device takes
    // over when there is none.
    private static IEnumerable<HouseholdOperationBody> MasterAfterRemoval(HouseholdMaster master, Guid removed, Guid me,
        string electedBy)
    {
        var concerned = master.ActiveDevice == removed || master.OutgoingDevice == removed
            || master.Election?.DeviceId == removed;
        if (!concerned) yield break;
        var electionId = Guid.NewGuid();
        var keep = master.Pending && master.OutgoingDevice is { } outgoing && outgoing != removed ? outgoing : (Guid?)null;
        if (keep is { } still)
        {
            yield return new MasterElected(electionId, still, electedBy, MasterElectionKind.Planned);
            yield break;
        }
        yield return new MasterElected(electionId, me, electedBy, MasterElectionKind.Takeover);
        yield return new MasterActivated(electionId, me);
    }

    // Step H5a: whether the storage holds a newer household key than this
    // device (a device was removed elsewhere). False while not published.
    public async Task<bool> NeedsNewKeyAsync(CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is null) return false;
        return (await SyncKeys.KeyVersionsAsync(_transports.Create(identity.Storage), identity.HouseholdId, cancellationToken))
            .FirstOrDefault() > identity.KeyVersion;
    }

    // Step H5a (D-15 option A): the new household key after a removal on
    // another device, from the new passphrase or a code (mrpair2) shown by
    // a device that has it. The local household moves to the new
    // generation and this device's own operations the genesis lacks are
    // carried over. CryptographicException for a wrong passphrase;
    // SyncPairingExpiredException or CryptographicException for a code no
    // longer offered; HouseholdDeviceRemovedException for a removed device.
    public async Task<HouseholdSyncResult> RekeyAsync(HouseholdKeySource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is null) throw new InvalidOperationException("The household is not published.");
        var transport = _transports.Create(identity.Storage);

        HouseholdOffer? offer = null;
        SyncGroupKey obtained;
        try
        {
            switch (source)
            {
                case HouseholdKeySource.Passphrase passphrase:
                    obtained = await SyncKeys.ObtainAsync(transport, _cipher, identity.HouseholdId,
                        new SyncKeySource.Passphrase(passphrase.Value), _clock, cancellationToken);
                    break;
                case HouseholdKeySource.Code { Value: var code }:
                    if (code.HouseholdId != identity.HouseholdId)
                        throw new InvalidOperationException("The code belongs to another household.");
                    var file = await transport.ReadAsync(SyncLayout.Pairing(code.HouseholdId, code.DeviceId), cancellationToken)
                        ?? throw new SyncPairingExpiredException();
                    offer = HouseholdPairingFile.Parse(file).Open(_cipher, code, _clock.GetUtcNow());
                    obtained = await SyncKeys.ObtainAsync(transport, _cipher, identity.HouseholdId,
                        new SyncKeySource.Known(offer.KeyVersion, offer.Key), _clock, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(source));
            }
            if (obtained.KeyVersion <= identity.KeyVersion)
            {
                CryptographicOperations.ZeroMemory(obtained.Key);
                throw new InvalidOperationException("The key is not newer than the one this device holds.");
            }

            var carried = (await _store.ListOperationsAsync(cancellationToken))
                .Where(r => r.Timestamp.DeviceId == identity.DeviceId)
                .Select(Outgoing)
                .ToList();
            await JoinCoreAsync(transport, identity.Storage, identity.HouseholdId, obtained,
                identity.DeviceName ?? _options.DeviceName, cancellationToken, carried);
            foreach (var profile in offer?.Profiles ?? [])
            {
                await _keyring.AcceptAsync(profile.ProfileId,
                    new ProfileGroupKey(profile.GroupId, profile.KeyVersion, profile.Key), cancellationToken);
            }
        }
        finally
        {
            if (offer is not null)
            {
                CryptographicOperations.ZeroMemory(offer.Key);
                foreach (var profile in offer.Profiles) CryptographicOperations.ZeroMemory(profile.Key);
            }
        }
        return await RunAsync(cancellationToken);
    }

    // Step H4a (§7.2, §7.4): an outgoing master releases the election that
    // replaced it; an elected device activates when MasterRules allows it,
    // reading the outgoing master's last-seen time from its device record
    // only when that decides. True when an operation was recorded.
    private async Task<bool> UpdateMasterAsync(ISyncTransport transport, byte[] key, HouseholdIdentity identity,
        CancellationToken ct)
    {
        var master = await _household.MasterAsync(ct);
        var me = identity.DeviceId;
        if (MasterRules.ShouldRelease(me, master))
        {
            await _household.AppendAsync([new MasterReleased(master.Election!.ElectionId)], ct);
            return true;
        }
        if (master.Election is not { } election || election.DeviceId != me || !master.Pending) return false;

        DateTimeOffset? outgoingSeen = null;
        if (master.OutgoingDevice is { } outgoing && outgoing != me && master.Released != election.ElectionId)
        {
            outgoingSeen = (await SyncEngine.ReadRecordsAsync(transport, _cipher, key, AsGroup(identity), ct))
                .FirstOrDefault(r => r.DeviceId == outgoing)?.LastSeen;
        }
        if (!MasterRules.ShouldActivate(me, master, outgoingSeen, _clock.GetUtcNow(), _options.MasterLease,
                _options.MasterMargin))
        {
            return false;
        }
        // Step H4b (§7.2 step 3): an administrator confirms the handover on
        // this device first, except for the device that publishes the
        // household and for the active master elected again (nothing bound
        // to it changes).
        if (election.Kind != MasterElectionKind.Creation && master.OutgoingDevice != me
            && identity.ConfirmedElection != election.ElectionId)
        {
            return false;
        }
        await _household.AppendAsync([new MasterActivated(election.ElectionId, me)], ct);
        return true;
    }

    // Step H3d: the device records of the household, for the Devices list.
    // Empty while the household is not on a storage.
    public async Task<IReadOnlyList<DeviceRecordContent>> ListDevicesAsync(CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is null) return [];
        var key = _keys.Load(identity.HouseholdId, identity.KeyVersion)
            ?? throw new InvalidOperationException("The household key is not stored on this device.");
        try
        {
            return await SyncEngine.ReadRecordsAsync(_transports.Create(identity.Storage), _cipher, key, AsGroup(identity),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    // Step H3d: the households of a storage, for the join.
    public Task<IReadOnlyList<Guid>> ListHouseholdsAsync(SyncTarget target, CancellationToken cancellationToken)
        => HouseholdFile.ListAsync(_transports.Create(target), cancellationToken);

    public const string RecoveryPurpose = "Recovery";

    // recovery.<v>.wrap next to key.<v>.wrap (§9.1 of docs/SYNC-FORMAT.md).
    public static string RecoveryWrapPath(Guid householdId, int keyVersion) => $"{householdId:N}/recovery.{keyVersion}.wrap";

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
