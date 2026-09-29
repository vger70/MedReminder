using MedReminder.Application.Abstractions;
using MedReminder.Domain.Household;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Household;

// Records household operations (step H2): an HLC timestamp from this
// device, the operation in the log and the register versions it writes,
// one transaction per operation. The gate serializes writers of the one
// store of the installation, so two scopes never issue the same
// timestamp.
public sealed class HouseholdLog
{
    // Also taken by HouseholdSync while it adds remote operations, so a
    // local timestamp is issued after every timestamp already received.
    internal static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IHouseholdStore _store;
    private readonly TimeProvider _clock;

    public HouseholdLog(IHouseholdStore store, TimeProvider clock)
    {
        _store = store;
        _clock = clock;
    }

    public async Task AppendAsync(IReadOnlyList<HouseholdOperationBody> operations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0) return;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var identity = await _store.EnsureCreatedAsync(cancellationToken);
            var last = await _store.GetLatestTimestampAsync(cancellationToken);
            foreach (var body in operations)
            {
                var timestamp = HybridClock.Tick(last, _clock.GetUtcNow().ToUnixTimeMilliseconds(), identity.DeviceId);
                last = timestamp;
                var (type, payload) = HouseholdOperationCodec.Serialize(body);
                var record = new HouseholdOperationRecord(Guid.NewGuid(), timestamp, identity.Generation, type,
                    HouseholdOperationCodec.SchemaVersionOf(body), body.ProfileId, payload);
                var writes = HouseholdRegisters.WritesOf(body)
                    .Select(w => new HouseholdRegisterVersion(body.ProfileId, w.Register, timestamp, w.Value))
                    .ToList();
                await _store.AppendAsync(record, writes, cancellationToken);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    // The winning value of each installation setting (step H2b). A secret
    // setting's value is still protected (HouseholdSettingChanged).
    public async Task<IReadOnlyDictionary<string, string?>> SettingsAsync(CancellationToken cancellationToken)
        => HouseholdRegisters.Settings((await _store.ListRegistersAsync(cancellationToken))
            .Select(v => (v.ProfileId, v.Register, v.Version, v.Value)));

    // Step H3b: device public keys, recovery public key, grants, escrows.
    public async Task<HouseholdKeys> KeysAsync(CancellationToken cancellationToken)
        => HouseholdRegisters.Keys((await _store.ListRegistersAsync(cancellationToken))
            .Select(v => (v.ProfileId, v.Register, v.Version, v.Value)));

    // The profiles of the household, from the winning register versions.
    public async Task<IReadOnlyList<HouseholdProfile>> ProfilesAsync(CancellationToken cancellationToken)
        => HouseholdRegisters.Profiles((await _store.ListRegistersAsync(cancellationToken))
            .Select(v => (v.ProfileId, v.Register, v.Version, v.Value)));
}
