using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Household;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Household;

public enum HandoverProfileState
{
    // Already on this device.
    Here,

    // Granted to this device: the handover downloads it.
    Granted,

    // Neither: the household passphrase recovers it from the escrow.
    Missing,
}

public sealed record HandoverProfile(string ProfileId, string DisplayName, HandoverProfileState State);

// A pending election naming this device, not yet confirmed here.
public sealed record HandoverView(MasterElection Election, Guid? OutgoingDevice, IReadOnlyList<HandoverProfile> Profiles);

// The handover wizard of the elected device (household step H4b;
// docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §7.2 step 3, §7.3):
// what the device must hold before it becomes the master, and the
// administrator's confirmation, after which the next household run
// activates the election (MasterRules still decide when). The
// device-bound steps (SMTP test, cloud-backup sign-in and passphrase) are
// the window's; the profiles come through JoinInstallation.
public sealed class MasterHandover
{
    private readonly IHouseholdStore _store;
    private readonly HouseholdLog _household;
    private readonly HouseholdKeyring _keyring;
    private readonly IProfileRegistry _registry;
    private readonly ICurrentProfile _current;
    private readonly ILogger<MasterHandover> _log;

    public MasterHandover(IHouseholdStore store, HouseholdLog household, HouseholdKeyring keyring,
        IProfileRegistry registry, ICurrentProfile current, ILogger<MasterHandover> log)
    {
        _store = store;
        _household = household;
        _keyring = keyring;
        _registry = registry;
        _current = current;
        _log = log;
    }

    // Null when no election naming this device waits for a confirmation.
    public async Task<HandoverView?> PendingAsync(CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        var master = await _household.MasterAsync(cancellationToken);
        if (master.Election is not { } election || election.DeviceId != identity.DeviceId || !master.Pending
            || election.Kind == MasterElectionKind.Creation || master.OutgoingDevice == identity.DeviceId
            || identity.ConfirmedElection == election.ElectionId)
        {
            return null;
        }

        var keys = await _keyring.KeysAsync(cancellationToken);
        var profiles = (await _household.ProfilesAsync(cancellationToken))
            .Select(p => new HandoverProfile(p.ProfileId, p.DisplayName,
                _registry.GetById(p.ProfileId) is not null ? HandoverProfileState.Here
                : keys.Grants.ContainsKey((p.ProfileId, identity.DeviceId)) ? HandoverProfileState.Granted
                : HandoverProfileState.Missing))
            .ToList();
        return new HandoverView(election, master.OutgoingDevice, profiles);
    }

    // ProfileAdministrationException(NotAdmin) for a user profile;
    // InvalidOperationException when the election is no longer the pending
    // one for this device (a newer election replaced it).
    public async Task ConfirmAsync(Guid electionId, CancellationToken cancellationToken)
    {
        ProfileAdministration.RequireAdmin(_current);
        var pending = await PendingAsync(cancellationToken);
        if (pending?.Election.ElectionId != electionId)
            throw new InvalidOperationException("The election is no longer pending on this device.");
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        await _store.SaveIdentityAsync(identity with { ConfirmedElection = electionId }, cancellationToken);
        _log.LogInformation("Profile {ProfileId} confirmed the master handover of election {ElectionId} on this device.",
            _current.Id, electionId);
    }
}
