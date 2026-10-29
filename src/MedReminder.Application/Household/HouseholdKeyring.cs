using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Domain.Household;

namespace MedReminder.Application.Household;

// Keys of the household (household step H3b; docs/analysis/
// ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §4.4):
//
//   - this device's key pair, whose public key the household holds;
//   - the recovery key pair, whose private key only the household
//     passphrase opens (recovery.<v>.wrap on the storage);
//   - for each profile sync group, a grant per device that holds the
//     profile (the group key wrapped for that device) and an escrow (the
//     group key wrapped for the recovery key).
//
// A device holds a profile when its key is in the profile's
// sync.protected; adopting records the grant to itself and the escrow
// the household does not have yet, so existing groups (B.1, v2.8–v2.10)
// join the household without a new key.
public sealed class HouseholdKeyring
{
    private readonly HouseholdLog _household;
    private readonly IHouseholdStore _store;
    private readonly IDeviceKeyStore _deviceKeys;
    private readonly IProfileGroupKeys _groupKeys;
    private readonly IProfileRegistry _registry;
    private readonly IArchiveCipher _cipher;

    public HouseholdKeyring(HouseholdLog household, IHouseholdStore store, IDeviceKeyStore deviceKeys,
        IProfileGroupKeys groupKeys, IProfileRegistry registry, IArchiveCipher cipher)
    {
        _household = household;
        _store = store;
        _deviceKeys = deviceKeys;
        _groupKeys = groupKeys;
        _registry = registry;
        _cipher = cipher;
    }

    // Creates this device's key pair on first use and records its public
    // key when the household does not hold it (a new device, or after a
    // join replaced the household). True when an operation was recorded.
    public async Task<bool> EnsureDeviceKeyAsync(CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        var privateKey = _deviceKeys.LoadPrivateKey();
        if (privateKey is null)
        {
            (privateKey, _) = HouseholdKeyWrap.CreateKeyPair();
            _deviceKeys.SavePrivateKey(privateKey);
        }
        var publicKey = HouseholdKeyWrap.PublicKeyOf(privateKey);
        var keys = await _household.KeysAsync(cancellationToken);
        if (keys.DevicePublicKeys.TryGetValue(identity.DeviceId, out var held) && held == publicKey) return false;
        // Issue #88: a device that left publishes no key again while it
        // finishes leaving.
        if (keys.HasLeft(identity.DeviceId)) return false;
        await _household.AppendAsync([new DeviceKeyPublished(identity.DeviceId, publicKey)], cancellationToken);
        return true;
    }

    // A new recovery key pair: the public key is recorded, the private key
    // is returned for the caller to wrap with the household passphrase.
    public async Task<string> CreateRecoveryKeyAsync(int keyVersion, CancellationToken cancellationToken)
    {
        var (privateKey, publicKey) = HouseholdKeyWrap.CreateKeyPair();
        await _household.AppendAsync([new RecoveryKeyPublished(keyVersion, publicKey)], cancellationToken);
        return privateKey;
    }

    // Records, for every synced profile of this installation, the grant
    // to this device and the escrow the household lacks for the current
    // group key. A grant or escrow of the same group with a newer key is
    // kept: after a rotation elsewhere (step H5b) this device runs the
    // household before it takes the new key, and its old key must not
    // replace the grant it is waiting for. A device still holding the
    // profile through a grant of an older key of the group (replaced
    // before that rule) is granted the current key again. Returns the
    // number of operations recorded.
    public async Task<int> AdoptProfileGroupsAsync(CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        var keys = await _household.KeysAsync(cancellationToken);
        keys.DevicePublicKeys.TryGetValue(identity.DeviceId, out var ownPublicKey);
        var operations = new List<HouseholdOperationBody>();
        foreach (var profile in _registry.ListProfiles())
        {
            if (_groupKeys.Load(profile.Id) is not { } group) continue;
            try
            {
                if (ownPublicKey is not null
                    && !Covers(keys.Grants.GetValueOrDefault((profile.Id, identity.DeviceId)), group))
                {
                    operations.Add(new ProfileKeyGranted(profile.Id, identity.DeviceId, group.GroupId, group.KeyVersion,
                        HouseholdKeyWrap.Wrap(_cipher, ownPublicKey, group.Key,
                            HouseholdKeyWrap.GrantPurpose(group.GroupId, group.KeyVersion, identity.DeviceId))));
                }
                foreach (var (device, publicKey) in keys.DevicePublicKeys)
                {
                    if (device == identity.DeviceId
                        || keys.Grants.GetValueOrDefault((profile.Id, device)) is not { } other
                        || other.GroupId != group.GroupId || other.KeyVersion >= group.KeyVersion)
                    {
                        continue;
                    }
                    operations.Add(new ProfileKeyGranted(profile.Id, device, group.GroupId, group.KeyVersion,
                        HouseholdKeyWrap.Wrap(_cipher, publicKey, group.Key,
                            HouseholdKeyWrap.GrantPurpose(group.GroupId, group.KeyVersion, device))));
                }
                if (keys.RecoveryPublicKey is { } recovery && !Covers(keys.Escrows.GetValueOrDefault(profile.Id), group))
                {
                    operations.Add(new ProfileKeyEscrowed(profile.Id, group.GroupId, group.KeyVersion,
                        HouseholdKeyWrap.Wrap(_cipher, recovery.Wrapped, group.Key,
                            HouseholdKeyWrap.EscrowPurpose(group.GroupId, group.KeyVersion))));
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(group.Key);
            }
        }
        await _household.AppendAsync(operations, cancellationToken);
        return operations.Count;
    }

    // Gives a profile this device holds to another device of the
    // household. InvalidOperationException when this device does not hold
    // the profile's key or the other device has no public key yet.
    public async Task GrantAsync(string profileId, Guid deviceId, CancellationToken cancellationToken)
    {
        var group = _groupKeys.Load(profileId)
            ?? throw new InvalidOperationException("This device does not hold the key of the profile.");
        try
        {
            await GrantCoreAsync(profileId, deviceId, group, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(group.Key);
        }
    }

    // Step H3c: records the grant to this device of a group key received
    // with a pairing offer or opened from the escrow, so the device keeps
    // the profile after the offer ends. The caller zeroes the key.
    public async Task AcceptAsync(string profileId, ProfileGroupKey group, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        await GrantCoreAsync(profileId, identity.DeviceId, group, cancellationToken);
    }

    // Step H5b: grants a given group key (a new key after a rotation) to
    // another device. The caller zeroes the key.
    public Task GrantKeyAsync(string profileId, Guid deviceId, ProfileGroupKey group, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        return GrantCoreAsync(profileId, deviceId, group, cancellationToken);
    }

    // Step H5b: the group key granted to this device for a profile synced
    // here, when it is newer than the key this device uses: the profile's
    // group was rotated after a device removal. Null otherwise. The caller
    // zeroes the key.
    public async Task<ProfileGroupKey?> NewerGrantAsync(string profileId, Guid groupId, int keyVersion,
        CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        var keys = await _household.KeysAsync(cancellationToken);
        if (keys.Grants.GetValueOrDefault((profileId, identity.DeviceId)) is not { } grant
            || grant.GroupId != groupId || grant.KeyVersion <= keyVersion)
        {
            return null;
        }
        return await OpenGrantAsync(profileId, cancellationToken);
    }

    private async Task GrantCoreAsync(string profileId, Guid deviceId, ProfileGroupKey group, CancellationToken ct)
    {
        var keys = await _household.KeysAsync(ct);
        var publicKey = keys.DevicePublicKeys.GetValueOrDefault(deviceId)
            ?? throw new InvalidOperationException("The device has not published its key yet.");
        await _household.AppendAsync([new ProfileKeyGranted(profileId, deviceId, group.GroupId, group.KeyVersion,
            HouseholdKeyWrap.Wrap(_cipher, publicKey, group.Key,
                HouseholdKeyWrap.GrantPurpose(group.GroupId, group.KeyVersion, deviceId)))], ct);
    }

    // Step H5a: the escrow of every profile this device holds, for a new
    // recovery public key (a removal changes the recovery key with the
    // household passphrase). The caller records the operations.
    public async Task<IReadOnlyList<HouseholdOperationBody>> EscrowAllAsync(string recoveryPublicKey,
        CancellationToken cancellationToken)
    {
        var operations = new List<HouseholdOperationBody>();
        foreach (var profile in await _household.ProfilesAsync(cancellationToken))
        {
            if (_groupKeys.Load(profile.ProfileId) is not { } group) continue;
            try
            {
                operations.Add(new ProfileKeyEscrowed(profile.ProfileId, group.GroupId, group.KeyVersion,
                    HouseholdKeyWrap.Wrap(_cipher, recoveryPublicKey, group.Key,
                        HouseholdKeyWrap.EscrowPurpose(group.GroupId, group.KeyVersion))));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(group.Key);
            }
        }
        return operations;
    }

    // In retain-passphrase removal mode, recovery must not be recoverable by
    // anyone who knows the shared passphrase. Give the new recovery private
    // key only to devices still in the household, protected by their device
    // keys and the authenticated household log.
    public async Task GrantRecoveryKeyAsync(int keyVersion, string recoveryPrivateKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryPrivateKey);
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        var keys = await _household.KeysAsync(cancellationToken);
        var privateBytes = Convert.FromBase64String(recoveryPrivateKey);
        try
        {
            var grants = keys.DevicePublicKeys
                .Where(pair => !keys.IsRemoved(pair.Key))
                .Select(pair => (HouseholdOperationBody)new RecoveryKeyGranted(keyVersion, pair.Key,
                    HouseholdKeyWrap.Wrap(_cipher, pair.Value, privateBytes,
                        HouseholdKeyWrap.RecoveryGrantPurpose(keyVersion, pair.Key))))
                .ToList();
            if (!keys.DevicePublicKeys.ContainsKey(identity.DeviceId))
                throw new InvalidOperationException("This device has no household public key.");
            await _household.AppendAsync(grants, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateBytes);
        }
    }

    public async Task GrantHouseholdKeyAsync(int keyVersion, byte[] householdKey, CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        var keys = await _household.KeysAsync(cancellationToken);
        var grants = keys.DevicePublicKeys.Where(pair => !keys.IsRemoved(pair.Key))
            .Select(pair => (HouseholdOperationBody)new HouseholdKeyGranted(keyVersion, pair.Key,
                HouseholdKeyWrap.Wrap(_cipher, pair.Value, householdKey,
                    HouseholdKeyWrap.HouseholdKeyGrantPurpose(keyVersion, pair.Key)))).ToList();
        if (!keys.DevicePublicKeys.ContainsKey(identity.DeviceId))
            throw new InvalidOperationException("This device has no household public key.");
        await _household.AppendAsync(grants, cancellationToken);
    }

    public async Task<byte[]?> OpenHouseholdKeyGrantAsync(int keyVersion, CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        var keys = await _household.KeysAsync(cancellationToken);
        if (!keys.HouseholdKeyGrants.TryGetValue(identity.DeviceId, out var grant) || grant.KeyVersion != keyVersion)
            return null;
        var privateKey = _deviceKeys.LoadPrivateKey()
            ?? throw new InvalidOperationException("This device has no household key pair.");
        return HouseholdKeyWrap.Unwrap(_cipher, privateKey, grant.Wrapped,
            HouseholdKeyWrap.HouseholdKeyGrantPurpose(keyVersion, identity.DeviceId));
    }

    // Opens this device's generation-specific recovery grant. Null means
    // this device was not among the retained devices for that generation.
    public async Task<string?> OpenRecoveryGrantAsync(int keyVersion, CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        var keys = await _household.KeysAsync(cancellationToken);
        if (!keys.RecoveryGrants.TryGetValue(identity.DeviceId, out var grant)
            || grant.KeyVersion != keyVersion)
            return null;
        var privateKey = _deviceKeys.LoadPrivateKey()
            ?? throw new InvalidOperationException("This device has no household key pair.");
        var bytes = HouseholdKeyWrap.Unwrap(_cipher, privateKey, grant.Wrapped,
            HouseholdKeyWrap.RecoveryGrantPurpose(keyVersion, identity.DeviceId));
        try { return Convert.ToBase64String(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public Task RevokeAsync(string profileId, Guid deviceId, CancellationToken cancellationToken)
        => _household.AppendAsync([new ProfileKeyRevoked(profileId, deviceId)], cancellationToken);

    // The profile's group key granted to this device; null when there is
    // no grant for it. CryptographicException when the grant does not open
    // with this device's private key.
    public async Task<ProfileGroupKey?> OpenGrantAsync(string profileId, CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        var keys = await _household.KeysAsync(cancellationToken);
        if (keys.Grants.GetValueOrDefault((profileId, identity.DeviceId)) is not { } grant) return null;
        var privateKey = _deviceKeys.LoadPrivateKey()
            ?? throw new InvalidOperationException("This device has no household key pair.");
        return new ProfileGroupKey(grant.GroupId, grant.KeyVersion, HouseholdKeyWrap.Unwrap(_cipher, privateKey,
            grant.Wrapped, HouseholdKeyWrap.GrantPurpose(grant.GroupId, grant.KeyVersion, identity.DeviceId)));
    }

    // The profile's group key from its escrow, with the recovery private
    // key (opened with the household passphrase by HouseholdSync). Null
    // when the household holds no escrow for the profile.
    public async Task<ProfileGroupKey?> OpenEscrowAsync(string profileId, string recoveryPrivateKey,
        CancellationToken cancellationToken)
    {
        var keys = await _household.KeysAsync(cancellationToken);
        if (keys.Escrows.GetValueOrDefault(profileId) is not { } escrow) return null;
        return new ProfileGroupKey(escrow.GroupId, escrow.KeyVersion, HouseholdKeyWrap.Unwrap(_cipher, recoveryPrivateKey,
            escrow.Wrapped, HouseholdKeyWrap.EscrowPurpose(escrow.GroupId, escrow.KeyVersion)));
    }

    public Task<HouseholdKeys> KeysAsync(CancellationToken cancellationToken) => _household.KeysAsync(cancellationToken);

    // The same key, or a newer key of the same group.
    private static bool Covers(HouseholdWrappedKey? held, ProfileGroupKey group)
        => held is not null && held.GroupId == group.GroupId && held.KeyVersion >= group.KeyVersion;
}
