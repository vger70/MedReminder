using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Household;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Household;

// What this device is in its household, for the leave dialog.
public enum HouseholdLeaveRole
{
    // Not in a published household: nothing to leave.
    NotPublished,
    // Another device is or becomes the master: the device leaves.
    Member,
    // The master, with other devices: it hands the role over or dissolves.
    Master,
    // No other device: leaving dissolves the household.
    Alone,
    // Dissolved or removed elsewhere, or behind a new key: only the local
    // household and profiles are left; nothing is published.
    Ended,
}

public enum HouseholdLeaveKind
{
    Leave,
    HandOver,
    Dissolve,
}

public enum HouseholdLeaveChoice
{
    Keep,
    Delete,
}

// OnlyHere: the profile has a sync group that no other device of the
// household holds; it leaves the household, and its group the storage,
// with this device. A profile without a group stays in the household.
public sealed record HouseholdLeaveProfile(string ProfileId, string DisplayName, bool OnlyHere);

public sealed record HouseholdLeaveDevice(Guid DeviceId, string Name);

// Dissolved: the household was dissolved (Role is Ended).
public sealed record HouseholdLeavePlan(
    HouseholdLeaveRole Role,
    IReadOnlyList<HouseholdLeaveDevice> OtherDevices,
    IReadOnlyList<HouseholdLeaveProfile> Profiles,
    bool Dissolved = false);

// Profiles: the choice for each household profile on this device; a
// profile left out is kept. NewMaster: the device that takes the master
// role over, for HandOver.
public sealed record HouseholdLeaveRequest(
    HouseholdLeaveKind Kind,
    IReadOnlyDictionary<string, HouseholdLeaveChoice> Profiles,
    Guid? NewMaster = null);

// Leaving the household (issue #88 of vger70/medreminder-mobile):
//
//   - a member records DeviceLeft. Its profiles that no other device holds
//     are removed from the household (ProfileRemoved) and their sync
//     groups deleted from the storage. Leaving does not change the
//     household key: an administrator removes the device afterwards
//     (RemoveDevice) to change it;
//   - the master first hands the role over: an election of the chosen
//     device, released at once, so that device activates when an
//     administrator confirms the handover there. Until then no device
//     sends email or runs the cloud backup;
//   - an administrator dissolves the household: a tombstone sealed with
//     the household key, then every file of the household and of its
//     profile groups is deleted. The other devices find the tombstone at
//     their next run (HouseholdSyncResult.Dissolved) and leave as Ended;
//     a device without another one can only dissolve.
//
// Then, on this device, each household profile is kept with sync off or
// deleted (IHouseholdLeavingProfiles) and the household starts again as
// a household of one (HouseholdSync.ResetLocalAsync). The storage is
// changed before anything local, so a leave that stopped part-way is
// finished by leaving again. Nothing but ids is logged.
public sealed class LeaveHousehold
{
    private readonly IHouseholdStore _store;
    private readonly HouseholdSync _sync;
    private readonly HouseholdLog _household;
    private readonly ElectMaster _elect;
    private readonly IProfileRegistry _registry;
    private readonly IProfileGroupKeys _groupKeys;
    private readonly IHouseholdLeavingProfiles _profiles;
    private readonly ICurrentProfile _current;
    private readonly ILogger<LeaveHousehold> _log;

    public LeaveHousehold(IHouseholdStore store, HouseholdSync sync, HouseholdLog household, ElectMaster elect,
        IProfileRegistry registry, IProfileGroupKeys groupKeys, IHouseholdLeavingProfiles profiles,
        ICurrentProfile current, ILogger<LeaveHousehold> log)
    {
        _store = store;
        _sync = sync;
        _household = household;
        _elect = elect;
        _registry = registry;
        _groupKeys = groupKeys;
        _profiles = profiles;
        _current = current;
        _log = log;
    }

    // A household run first, so the plan reflects the others' latest
    // changes; the exceptions of HouseholdSync.RunAsync (offline, a
    // provider error) are the caller's to show.
    public async Task<HouseholdLeavePlan> PlanAsync(CancellationToken cancellationToken)
    {
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is null) return new HouseholdLeavePlan(HouseholdLeaveRole.NotPublished, [], []);
        var run = await _sync.RunAsync(cancellationToken);
        return await PlanAfterAsync(identity.DeviceId, run, cancellationToken);
    }

    // ProfileAdministrationException(NotAdmin) to hand over or dissolve
    // from a user profile; InvalidOperationException when the kind does
    // not fit the role (a master that just leaves, a member that
    // dissolves, a device alone that does not dissolve) or the new master
    // is not another device of the household.
    public async Task ExecuteAsync(HouseholdLeaveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profiles);
        var plan = await PlanAsync(cancellationToken);
        Validate(plan, request);

        if (plan.Role == HouseholdLeaveRole.Ended)
        {
            // An interrupted dissolution: any device that finds the
            // tombstone finishes the deletion, best effort.
            if (plan.Dissolved) await DeleteDissolvedAsync(swallow: true, cancellationToken);
        }
        else if (request.Kind == HouseholdLeaveKind.Dissolve)
        {
            await _sync.MarkDissolvedAsync(cancellationToken);
            await DeleteDissolvedAsync(swallow: false, cancellationToken);
        }
        else
        {
            await DepartAsync(plan, request, cancellationToken);
        }

        foreach (var profile in plan.Profiles)
        {
            var choice = request.Profiles.GetValueOrDefault(profile.ProfileId, HouseholdLeaveChoice.Keep);
            if (choice == HouseholdLeaveChoice.Delete) await _profiles.DeleteAsync(profile.ProfileId, cancellationToken);
            else await _profiles.KeepAsync(profile.ProfileId, cancellationToken);
        }
        var device = (await _store.EnsureCreatedAsync(cancellationToken)).DeviceId;
        await _sync.ResetLocalAsync(cancellationToken);
        _log.LogInformation("Device {DeviceId} left its household ({Kind}, {Role}); {Profiles} profiles handled.",
            device, request.Kind, plan.Role, plan.Profiles.Count);
    }

    private async Task<HouseholdLeavePlan> PlanAfterAsync(Guid me, HouseholdSyncResult run, CancellationToken ct)
    {
        var keys = await _household.KeysAsync(ct);
        var others = keys.DevicePublicKeys.Keys.Where(d => d != me).ToHashSet();
        var profiles = (await _household.ProfilesAsync(ct))
            .Where(p => _registry.GetById(p.ProfileId) is not null)
            .Select(p => new HouseholdLeaveProfile(p.ProfileId, p.DisplayName, OnlyHere(keys, p.ProfileId, me)))
            .ToList();
        if (run.Dissolved || run.Removed || run.NewKeyRequired)
        {
            return new HouseholdLeavePlan(HouseholdLeaveRole.Ended, [], profiles, run.Dissolved);
        }

        var names = new Dictionary<Guid, string>();
        try
        {
            foreach (var record in await _sync.ListDevicesAsync(ct)) names[record.DeviceId] = record.Name;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The records only name the devices.
        }
        var devices = others.Select(d => new HouseholdLeaveDevice(d, names.GetValueOrDefault(d, string.Empty)))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        var role = others.Count == 0 ? HouseholdLeaveRole.Alone
            : IsMaster(await _household.MasterAsync(ct), me) ? HouseholdLeaveRole.Master
            : HouseholdLeaveRole.Member;
        return new HouseholdLeavePlan(role, devices, profiles);
    }

    private bool OnlyHere(HouseholdKeys keys, string profileId, Guid me)
    {
        var grantees = keys.Grants.Keys.Where(g => g.ProfileId == profileId).Select(g => g.DeviceId).ToList();
        var local = _groupKeys.Load(profileId);
        if (local is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(local.Key);
        var synced = grantees.Contains(me) || local is not null;
        return synced && !grantees.Any(d => d != me && keys.DevicePublicKeys.ContainsKey(d));
    }

    // The active master, or the device a pending election names with no
    // master to fall back on. An outgoing master that already handed the
    // role over leaves as a member.
    private static bool IsMaster(HouseholdMaster master, Guid me)
        => master.ActiveDevice == me
           || (master.Pending && master.Election?.DeviceId == me && master.OutgoingDevice is null);

    private void Validate(HouseholdLeavePlan plan, HouseholdLeaveRequest request)
    {
        switch (plan.Role)
        {
            case HouseholdLeaveRole.NotPublished:
                throw new InvalidOperationException("The household is not published.");
            case HouseholdLeaveRole.Ended:
                return;
            case HouseholdLeaveRole.Alone when request.Kind != HouseholdLeaveKind.Dissolve:
                throw new InvalidOperationException("A device alone in its household dissolves it.");
            case HouseholdLeaveRole.Master when request.Kind == HouseholdLeaveKind.Leave:
                throw new InvalidOperationException("The master hands its role over or dissolves the household.");
            case HouseholdLeaveRole.Member when request.Kind != HouseholdLeaveKind.Leave:
                throw new InvalidOperationException("Only the master hands its role over or dissolves the household.");
        }
        if (request.Kind is HouseholdLeaveKind.HandOver or HouseholdLeaveKind.Dissolve)
        {
            ProfileAdministration.RequireAdmin(_current);
        }
        if (request.Kind == HouseholdLeaveKind.HandOver
            && (request.NewMaster is not { } next || plan.OtherDevices.All(d => d.DeviceId != next)))
        {
            throw new InvalidOperationException("The new master is not another device of the household.");
        }
    }

    private async Task DepartAsync(HouseholdLeavePlan plan, HouseholdLeaveRequest request, CancellationToken ct)
    {
        var me = (await _store.EnsureCreatedAsync(ct)).DeviceId;
        if (request.Kind == HouseholdLeaveKind.HandOver)
        {
            // The run publishes the election and, applying it, this
            // device's release.
            await _elect.ExecuteAsync(request.NewMaster!.Value, ct);
            await _sync.RunAsync(ct);
        }
        else if (await _household.MasterAsync(ct) is { Pending: true, Election: { } election, OutgoingDevice: { } outgoing }
                 && election.DeviceId == me && outgoing != me)
        {
            // An election naming this device, not activated: the outgoing
            // master is elected again and stays, without a handover.
            await _household.AppendAsync(
                [new MasterElected(Guid.NewGuid(), outgoing, _current.Id, MasterElectionKind.Planned)], ct);
            await _sync.RunAsync(ct);
        }

        // Profiles only this device holds leave the household first, in a
        // segment of their own: an app that does not know DeviceLeft stops
        // reading this device's segments at that one. Read again after the
        // election, which granted this device's profiles to the new master.
        var keys = await _household.KeysAsync(ct);
        var alone = plan.Profiles.Where(p => OnlyHere(keys, p.ProfileId, me)).Select(p => p.ProfileId).ToList();
        var groups = await GroupsOfAsync(alone, ct);
        if (alone.Count > 0)
        {
            await _household.AppendAsync([.. alone.Select(id => new ProfileRemoved(id))], ct);
            await _sync.RunAsync(ct);
            await _sync.DeleteRemoteAsync(groups, household: false, ct);
        }
        await _household.AppendAsync([new DeviceLeft(me)], ct);
        await _sync.RunAsync(ct);
        await _sync.DeleteOwnFilesAsync(ct);
    }

    private async Task DeleteDissolvedAsync(bool swallow, CancellationToken ct)
    {
        try
        {
            var profiles = (await _household.ProfilesAsync(ct)).Select(p => p.ProfileId).ToList();
            await _sync.DeleteRemoteAsync(await GroupsOfAsync(profiles, ct), household: true, ct);
        }
        catch (Exception ex) when (swallow && ex is not OperationCanceledException)
        {
            // The device that dissolved deletes the files; another device
            // only finishes what it left.
        }
    }

    // The sync groups of the profiles: the group key here, and the groups
    // the household's grants and escrows name.
    private async Task<IReadOnlyCollection<Guid>> GroupsOfAsync(IReadOnlyCollection<string> profileIds, CancellationToken ct)
    {
        var keys = await _household.KeysAsync(ct);
        var groups = new HashSet<Guid>();
        foreach (var profileId in profileIds)
        {
            if (_groupKeys.Load(profileId) is { } local)
            {
                groups.Add(local.GroupId);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(local.Key);
            }
            if (keys.Escrows.TryGetValue(profileId, out var escrow)) groups.Add(escrow.GroupId);
            foreach (var grant in keys.Grants.Where(g => g.Key.ProfileId == profileId)) groups.Add(grant.Value.GroupId);
        }
        groups.Remove(Guid.Empty);
        return groups;
    }
}
