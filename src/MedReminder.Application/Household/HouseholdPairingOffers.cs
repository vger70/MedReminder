using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Application.Household.Remote;
using MedReminder.Application.Sync;
using MedReminder.Application.Sync.Remote;
using MedReminder.Application.UseCases;

namespace MedReminder.Application.Household;

public sealed record HouseholdPairingOffer(HouseholdPairingCode Code, DateTimeOffset ExpiresAt);

// Devices → Add a device (household step H3c; docs/analysis/
// ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §6.2): an admin selects the profiles
// the new device will hold; this device writes its offer file with the
// household key and the group keys of those profiles, encrypted with the
// random secret of an mrpair2 code, for 10 minutes. Creating the code is
// the admin's approval: the joining device grants the offered profiles to
// itself. The offer ends when the window closes (EndAsync) or expires. A
// new offer replaces the previous one.
public sealed class HouseholdPairingOffers
{
    public static readonly TimeSpan Lifetime = SyncPairingOffers.Lifetime;

    private readonly IHouseholdStore _store;
    private readonly IHouseholdKeyStore _keys;
    private readonly IProfileGroupKeys _groupKeys;
    private readonly HouseholdLog _household;
    private readonly ISyncTransportFactory _transports;
    private readonly IArchiveCipher _cipher;
    private readonly ICurrentProfile _current;
    private readonly TimeProvider _clock;

    public HouseholdPairingOffers(IHouseholdStore store, IHouseholdKeyStore keys, IProfileGroupKeys groupKeys,
        HouseholdLog household, ISyncTransportFactory transports, IArchiveCipher cipher, ICurrentProfile current,
        TimeProvider clock)
    {
        _store = store;
        _keys = keys;
        _groupKeys = groupKeys;
        _household = household;
        _transports = transports;
        _cipher = cipher;
        _current = current;
        _clock = clock;
    }

    // ProfileAdministrationException(NotAdmin) for a user profile;
    // InvalidOperationException when the household is not on a storage, a
    // profile is not in the household, or this device does not hold its key.
    public async Task<HouseholdPairingOffer> StartAsync(IReadOnlyCollection<string> profileIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileIds);
        ProfileAdministration.RequireAdmin(_current);
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is null) throw new InvalidOperationException("The household is not published.");
        // Step H5a: a device waiting for the new key would hand out the old one.
        if ((await SyncKeys.KeyVersionsAsync(_transports.Create(identity.Storage), identity.HouseholdId, cancellationToken))
                .FirstOrDefault() > identity.KeyVersion)
        {
            throw new InvalidOperationException("The household key was changed on another device; this device needs the new key first.");
        }

        var known = (await _household.ProfilesAsync(cancellationToken)).Select(p => p.ProfileId).ToHashSet(StringComparer.Ordinal);
        var profiles = new List<HouseholdOfferedProfile>();
        var key = _keys.Load(identity.HouseholdId, identity.KeyVersion)
            ?? throw new InvalidOperationException("The household key is not stored on this device.");
        try
        {
            foreach (var profileId in profileIds.Distinct(StringComparer.Ordinal))
            {
                if (!known.Contains(profileId))
                    throw new InvalidOperationException($"Profile {profileId} is not in the household.");
                var group = _groupKeys.Load(profileId)
                    ?? throw new InvalidOperationException($"This device does not hold the key of profile {profileId}.");
                profiles.Add(new HouseholdOfferedProfile(profileId, group.GroupId, group.KeyVersion, group.Key));
            }

            var code = new HouseholdPairingCode(identity.HouseholdId, identity.DeviceId, identity.Storage.Provider,
                RandomNumberGenerator.GetBytes(SyncPairingCode.SecretSize));
            var expiresAt = _clock.GetUtcNow() + Lifetime;
            var file = HouseholdPairingFile.Seal(_cipher, code, new HouseholdOffer(identity.KeyVersion, key, profiles),
                expiresAt);
            await _transports.Create(identity.Storage).WriteAsync(SyncLayout.Pairing(identity.HouseholdId, identity.DeviceId),
                file.ToBytes(), cancellationToken);
            return new HouseholdPairingOffer(code, expiresAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            foreach (var profile in profiles) CryptographicOperations.ZeroMemory(profile.Key);
        }
    }

    public async Task EndAsync(CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is null) return;
        await _transports.Create(identity.Storage).DeleteAsync(SyncLayout.Pairing(identity.HouseholdId, identity.DeviceId),
            cancellationToken);
    }
}
