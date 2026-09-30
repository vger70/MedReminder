using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Household.Remote;
using MedReminder.Application.Sync;
using MedReminder.Application.Sync.Remote;
using MedReminder.Domain.Household;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Household;

public enum JoinedProfileStatus
{
    // Built from its group and added to this device.
    Installed,

    // Already on this device, synced with the granted group: kept as is.
    AlreadyHere,

    // A local profile with the same id is not synced with the granted
    // group: left for the admin to decide (§6.4, step H3d).
    Conflict,

    // No grant for this device, or the profile left the household.
    NotGranted,

    // The build or the install failed; the join can be retried.
    Failed,
}

// Problem: an exception type name or a short reason, never a name or a key.
public sealed record JoinedProfile(string ProfileId, JoinedProfileStatus Status, string? Problem = null);

public sealed record JoinInstallationResult(HouseholdSyncResult Household, IReadOnlyList<JoinedProfile> Profiles);

public enum HouseholdApprovalError
{
    // The profile is not an administrator of the household.
    NotAdmin,

    WrongPin,
}

public sealed class HouseholdApprovalException(HouseholdApprovalError error)
    : InvalidOperationException($"Household approval refused: {error}.")
{
    public HouseholdApprovalError Error { get; } = error;
}

// Join an existing installation (household step H3c; docs/analysis/
// ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §6): the household first, then each
// profile granted to this device, built from its sync group in a staging
// folder and added to profiles.json under its household id.
//
//   - With a pairing code (mrpair2), the offer holds the household key and
//     the keys of the profiles an admin selected; creating the code was
//     the approval.
//   - With the household passphrase, the household is joined first
//     (JoinHouseholdAsync); an admin of the household then approves on
//     this device with the PIN of an admin profile and selects the
//     profiles, whose keys come from the escrow (ApproveAndGrantAsync);
//     InstallGrantedAsync then brings them.
//
// The profile groups are read from the storage of the household: an
// installation keeps its profile groups and its household in one storage
// (§6.3 page 3). A profile that fails is reported and can be retried with
// InstallGrantedAsync; the others stay installed. The household join itself
// replaces the local household store and is not staged.
//
// Logs carry ids and counters only (§6.5).
public sealed class JoinInstallation
{
    private readonly HouseholdSync _sync;
    private readonly HouseholdKeyring _keyring;
    private readonly HouseholdLog _household;
    private readonly IHouseholdStore _store;
    private readonly IProfileRegistry _registry;
    private readonly IProfileGroupKeys _groupKeys;
    private readonly IHouseholdProfileInstaller _installer;
    private readonly JoinSyncGroup _join;
    private readonly ISyncTransportFactory _transports;
    private readonly ILogger<JoinInstallation> _log;

    public JoinInstallation(HouseholdSync sync, HouseholdKeyring keyring, HouseholdLog household, IHouseholdStore store,
        IProfileRegistry registry, IProfileGroupKeys groupKeys, IHouseholdProfileInstaller installer, JoinSyncGroup join,
        ISyncTransportFactory transports, ILogger<JoinInstallation> log)
    {
        _sync = sync;
        _keyring = keyring;
        _household = household;
        _store = store;
        _registry = registry;
        _groupKeys = groupKeys;
        _installer = installer;
        _join = join;
        _transports = transports;
        _log = log;
    }

    // SyncPairingExpiredException when the offer is over,
    // CryptographicException when a newer offer replaced it.
    public async Task<JoinInstallationResult> JoinWithCodeAsync(SyncTarget target, HouseholdPairingCode code,
        string deviceName, CancellationToken cancellationToken)
    {
        var (household, granted) = await _sync.JoinAsync(target, code, deviceName, cancellationToken);
        _log.LogInformation("Joined household {HouseholdId} with a pairing code; {Count} profiles offered.",
            code.HouseholdId, granted.Count);
        return new JoinInstallationResult(household, await InstallGrantedAsync(granted, deviceName, cancellationToken));
    }

    // CryptographicException for a wrong passphrase.
    public async Task<HouseholdSyncResult> JoinHouseholdAsync(SyncTarget target, Guid householdId, char[] passphrase,
        string deviceName, CancellationToken cancellationToken)
    {
        var result = await _sync.JoinAsync(target, householdId, passphrase, deviceName, cancellationToken);
        _log.LogInformation("Joined household {HouseholdId} with the household passphrase.", householdId);
        return result;
    }

    // §6.3 page 5. A profile without a PIN approves without one (the
    // wizard warns). HouseholdApprovalException for a profile that is not an
    // administrator of the household or a wrong PIN; CryptographicException
    // for a wrong passphrase. Returns the profile ids granted.
    public async Task<IReadOnlyList<string>> ApproveAndGrantAsync(string adminProfileId, string? pin,
        IReadOnlyCollection<string> profileIds, char[] passphrase, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminProfileId);
        var admin = (await _household.ProfilesAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.ProfileId, adminProfileId, StringComparison.Ordinal));
        if (admin is null || admin.Role != HouseholdRole.Admin)
            throw new HouseholdApprovalException(HouseholdApprovalError.NotAdmin);
        if (HouseholdRegisters.TryParsePin(admin.Pin, out var hash, out var salt, out var iterations)
            && (pin is null || !new ProfilePinHash(hash, salt, iterations).Matches(pin)))
        {
            _log.LogWarning("Household join approval refused: wrong PIN for admin profile {ProfileId}.", adminProfileId);
            throw new HouseholdApprovalException(HouseholdApprovalError.WrongPin);
        }

        var granted = await _sync.GrantFromEscrowAsync(profileIds, passphrase, cancellationToken);
        _log.LogInformation("Admin profile {ProfileId} approved the join; {Count} profiles granted from the escrow.",
            adminProfileId, granted.Count);
        return granted;
    }

    // Brings each profile granted to this device. Also the retry of a
    // failed profile.
    public async Task<IReadOnlyList<JoinedProfile>> InstallGrantedAsync(IReadOnlyCollection<string> profileIds,
        string deviceName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileIds);
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is null) throw new InvalidOperationException("This device is not in a published household.");
        var profiles = (await _household.ProfilesAsync(cancellationToken))
            .ToDictionary(p => p.ProfileId, StringComparer.Ordinal);
        var transport = _transports.Create(identity.Storage);

        var results = new List<JoinedProfile>();
        foreach (var profileId in profileIds.Distinct(StringComparer.Ordinal))
        {
            var result = profiles.TryGetValue(profileId, out var profile)
                ? await InstallAsync(transport, identity.Storage, profile, deviceName, cancellationToken)
                : new JoinedProfile(profileId, JoinedProfileStatus.NotGranted);
            _log.LogInformation("Household join: profile {ProfileId} {Status}{Problem}.", profileId, result.Status,
                result.Problem is null ? string.Empty : " (" + result.Problem + ")");
            results.Add(result);
        }
        return results;
    }

    private async Task<JoinedProfile> InstallAsync(ISyncTransport transport, SyncTarget target, HouseholdProfile profile,
        string deviceName, CancellationToken ct)
    {
        var grant = await _keyring.OpenGrantAsync(profile.ProfileId, ct);
        if (grant is null) return new JoinedProfile(profile.ProfileId, JoinedProfileStatus.NotGranted);
        try
        {
            if (_registry.GetById(profile.ProfileId) is not null)
            {
                var local = _groupKeys.Load(profile.ProfileId);
                if (local is not null) CryptographicOperations.ZeroMemory(local.Key);
                return new JoinedProfile(profile.ProfileId,
                    local?.GroupId == grant.GroupId ? JoinedProfileStatus.AlreadyHere : JoinedProfileStatus.Conflict);
            }

            var staging = _installer.CreateStaging();
            try
            {
                var joined = await _join.ExecuteAsync(transport, grant.GroupId,
                    new SyncKeySource.Known(grant.KeyVersion, grant.Key), staging.DatabasePath, target, ct, deviceName);
                try
                {
                    _installer.Install(staging, profile.ProfileId, joined.Settings, joined.Key);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(joined.Key);
                }
            }
            catch
            {
                _installer.Discard(staging);
                throw;
            }

            _registry.Register(profile.ProfileId, profile.DisplayName,
                profile.Role == HouseholdRole.Admin ? ProfileRole.Admin : ProfileRole.User,
                HouseholdRegisters.TryParsePin(profile.Pin, out var hash, out var salt, out var iterations)
                    ? new ProfilePinHash(hash, salt, iterations)
                    : null);
            return new JoinedProfile(profile.ProfileId, JoinedProfileStatus.Installed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new JoinedProfile(profile.ProfileId, JoinedProfileStatus.Failed, ex.GetType().Name);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(grant.Key);
        }
    }
}
