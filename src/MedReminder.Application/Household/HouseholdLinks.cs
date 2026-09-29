using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Household;

public enum HouseholdLinkState
{
    // No household claims the profile group.
    None,

    // The household of this installation is the earliest claim.
    ThisHousehold,

    // Another household claimed the group first: this installation joins
    // it instead of keeping its own (§11).
    OtherHousehold,
}

public sealed record HouseholdLinkStatus(HouseholdLinkState State, Guid? HouseholdId);

// The household that adopted the open profile's sync group (household step
// H3c; docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §11). Adoption
// (HouseholdKeyring) runs for every profile of the installation from the
// household side; the claim is recorded here, in the profile group, by the
// profile's own scope before its sync run, since a profile database is
// written only by the scope that has it open (in-process database gate).
public sealed class HouseholdLinks
{
    private readonly IHouseholdStore _store;
    private readonly HouseholdLog _household;
    private readonly ICurrentProfile _current;
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncOperationRepository _operations;
    private readonly IOperationLog _log;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public HouseholdLinks(IHouseholdStore store, HouseholdLog household, ICurrentProfile current,
        ISyncSettingsStore settings, ISyncOperationRepository operations, IOperationLog log, IUnitOfWork uow,
        TimeProvider clock)
    {
        _store = store;
        _household = household;
        _current = current;
        _settings = settings;
        _operations = operations;
        _log = log;
        _uow = uow;
        _clock = clock;
    }

    // Records HouseholdLinked once per household, when the household of
    // this installation is published and holds the open profile with its
    // current group (a grant or an escrow for that group). True when an
    // operation was recorded.
    public async Task<bool> RecordAsync(CancellationToken cancellationToken)
    {
        if (_settings.Load() is not { } settings) return false;
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is null) return false;
        var keys = await _household.KeysAsync(cancellationToken);
        var adopted = keys.Escrows.GetValueOrDefault(_current.Id)?.GroupId == settings.GroupId
            || keys.Grants.GetValueOrDefault((_current.Id, identity.DeviceId))?.GroupId == settings.GroupId;
        if (!adopted) return false;

        return await WriteGate.RunExclusiveAsync(async ct =>
        {
            if ((await ClaimsAsync(ct)).Any(c => c.HouseholdId == identity.HouseholdId)) return false;
            await _log.AppendAsync([new HouseholdLinked(identity.HouseholdId, _clock.GetUtcNow())], ct);
            await _uow.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);
    }

    // The earliest claim by HLC, against this installation's household.
    public async Task<HouseholdLinkStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var first = (await ClaimsAsync(cancellationToken)).FirstOrDefault();
        if (first is null) return new HouseholdLinkStatus(HouseholdLinkState.None, null);
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        return new HouseholdLinkStatus(
            first.HouseholdId == identity.HouseholdId ? HouseholdLinkState.ThisHousehold : HouseholdLinkState.OtherHousehold,
            first.HouseholdId);
    }

    // In HLC order.
    private async Task<IReadOnlyList<HouseholdLinked>> ClaimsAsync(CancellationToken ct)
        => [.. (await _operations.ListOfTypeAsync(nameof(HouseholdLinked), ct))
            .Select(o => (HouseholdLinked)OperationCodec.Deserialize(o.Type, o.SchemaVersion, o.Payload))];
}
