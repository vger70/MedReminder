using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Application.UseCases;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Household;

// The profiles of the removed device this device does not hold: their
// groups cannot be rotated here. The master holds every profile (C4).
public sealed class ProfilesNotHeldException(IReadOnlyList<string> profileIds)
    : InvalidOperationException("This device does not hold every profile of the device to remove.")
{
    public IReadOnlyList<string> ProfileIds { get; } = profileIds;
}

// Tools → Installation → Devices → Remove device (household steps H5a and
// H5b; docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §9):
//
//   1. the household moves to a new key, passphrase, recovery key and
//      generation (HouseholdSync.RemoveDeviceAsync, step H5a);
//   2. the key of every profile group the removed device held is rotated,
//      with a random passphrase never shown (IProfileGroupRotation);
//   3. each new key is granted to the other devices that held the profile;
//      the next household run grants it to this device and escrows it for
//      the new recovery key (HouseholdKeyring.AdoptProfileGroupsAsync).
//
// The remaining devices take the new household key with the new
// passphrase or a code (D-15 option A), then the new profile keys from
// their grants without typing anything.
public sealed class RemoveDevice
{
    private readonly HouseholdSync _sync;
    private readonly HouseholdKeyring _keyring;
    private readonly IProfileGroupKeys _groupKeys;
    private readonly IProfileGroupRotation _rotation;
    private readonly ICurrentProfile _current;
    private readonly ILogger<RemoveDevice> _log;

    public RemoveDevice(HouseholdSync sync, HouseholdKeyring keyring, IProfileGroupKeys groupKeys,
        IProfileGroupRotation rotation, ICurrentProfile current, ILogger<RemoveDevice> log)
    {
        _sync = sync;
        _keyring = keyring;
        _groupKeys = groupKeys;
        _rotation = rotation;
        _current = current;
        _log = log;
    }

    // ProfileAdministrationException(NotAdmin); ProfilesNotHeldException
    // before anything changes; the exceptions of RemoveDeviceAsync. Returns
    // the number of profile groups rotated.
    public async Task<int> ExecuteAsync(Guid deviceId, char[] passphrase, CancellationToken cancellationToken,
        Argon2Params? kdf = null)
    {
        ProfileAdministration.RequireAdmin(_current);
        var keys = await _keyring.KeysAsync(cancellationToken);
        var held = keys.Grants.Keys.Where(g => g.DeviceId == deviceId).Select(g => g.ProfileId).Distinct().ToList();
        var missing = held.Where(p => !Holds(p)).ToList();
        if (missing.Count > 0) throw new ProfilesNotHeldException(missing);
        // Who else held each profile, before the removal changes the grants.
        var holders = held.ToDictionary(p => p, p => keys.Grants.Keys
            .Where(g => g.ProfileId == p && g.DeviceId != deviceId).Select(g => g.DeviceId).ToList());

        await _sync.RemoveDeviceAsync(deviceId, passphrase, _current.Id, cancellationToken, kdf);
        var active = (await _keyring.KeysAsync(cancellationToken)).DevicePublicKeys;
        foreach (var profileId in held)
        {
            var key = await _rotation.RotateAsync(profileId, cancellationToken);
            try
            {
                foreach (var device in holders[profileId].Where(active.ContainsKey))
                {
                    await _keyring.GrantKeyAsync(profileId, device, key, cancellationToken);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key.Key);
            }
        }
        // Grants to this device and escrows for the new recovery key.
        await _sync.RunAsync(cancellationToken);
        _log.LogInformation("Device {DeviceId} removed from the household; {Count} profile groups rotated.",
            deviceId, held.Count);
        return held.Count;
    }

    private bool Holds(string profileId)
    {
        if (_groupKeys.Load(profileId) is not { } group) return false;
        CryptographicOperations.ZeroMemory(group.Key);
        return true;
    }
}
