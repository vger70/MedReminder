using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Household;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Household;

// What the installation window shows about the master (step H4a).
public sealed record MasterView(
    HouseholdMaster Master,
    Guid ThisDevice,
    bool SendsEmail,
    // The active master is this device but its lease ran out: the
    // household has not synced for too long.
    bool LeaseExpired);

// IMasterRole over the household (household step H4a; docs/analysis/
// ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §7): MasterRules applied to the
// master registers, this device's identity and its last household sync.
public sealed class HouseholdMasterRole : IMasterRole
{
    private readonly IHouseholdStore _store;
    private readonly HouseholdLog _household;
    private readonly TimeProvider _clock;
    private readonly SyncEngineOptions _options;

    public HouseholdMasterRole(IHouseholdStore store, HouseholdLog household, TimeProvider clock,
        SyncEngineOptions? options = null)
    {
        _store = store;
        _household = household;
        _clock = clock;
        _options = options ?? new SyncEngineOptions();
    }

    public async Task<bool> SendsEmailAsync(CancellationToken cancellationToken)
        => (await DescribeAsync(cancellationToken)).SendsEmail;

    public async Task<MasterView> DescribeAsync(CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        var master = await _household.MasterAsync(cancellationToken);
        var published = identity.Storage is not null;
        var sends = MasterRules.SendsEmail(identity.DeviceId, master, published, identity.LastSyncedAt,
            _clock.GetUtcNow(), _options.MasterLease);
        var leaseExpired = master.ActiveDevice == identity.DeviceId && published && !sends;
        return new MasterView(master, identity.DeviceId, sends, leaseExpired);
    }
}

// Tools → Installation → Devices → Make master (step H4a, §7.2 step 1): an
// administrator elects a device of the household. The elected device
// activates on its own when MasterRules allows it (step H4b adds the
// handover wizard before that). Electing the active master again cancels
// a pending election.
public sealed class ElectMaster
{
    private readonly IHouseholdStore _store;
    private readonly HouseholdLog _household;
    private readonly ICurrentProfile _current;
    private readonly ILogger<ElectMaster> _log;

    public ElectMaster(IHouseholdStore store, HouseholdLog household, ICurrentProfile current, ILogger<ElectMaster> log)
    {
        _store = store;
        _household = household;
        _current = current;
        _log = log;
    }

    // ProfileAdministrationException(NotAdmin) for a user profile;
    // InvalidOperationException when the household is not published or the
    // device is not one of it (no published key).
    public async Task ExecuteAsync(Guid deviceId, CancellationToken cancellationToken,
        string kind = MasterElectionKind.Planned)
    {
        ProfileAdministration.RequireAdmin(_current);
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is null) throw new InvalidOperationException("The household is not published.");
        if (deviceId != identity.DeviceId
            && !(await _household.KeysAsync(cancellationToken)).DevicePublicKeys.ContainsKey(deviceId))
        {
            throw new InvalidOperationException("The device is not part of the household.");
        }
        var electionId = Guid.NewGuid();
        await _household.AppendAsync([new MasterElected(electionId, deviceId, _current.Id, kind)], cancellationToken);
        _log.LogInformation("Profile {ProfileId} elected device {DeviceId} as master ({Kind}, election {ElectionId}).",
            _current.Id, deviceId, kind, electionId);
    }
}
