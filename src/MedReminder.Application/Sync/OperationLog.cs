using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

// IOperationLog over the SyncOperations table (B.1 Phase 3a,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.1, §7.2).
//
// Clock state: the greatest timestamp in the log, read once per scope
// and then advanced in memory, so several operations of one use case
// get increasing timestamps before they are saved. Callers hold
// WriteGate, so no other writer commits in between.
//
// From Phase 3b each operation also records its register versions
// (SyncRegisters), stamped with the base version its writer held, so the
// apply step on other devices can tell a concurrent write from a later
// one.
public sealed class OperationLog : IOperationLog
{
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncOperationRepository _operations;
    private readonly SyncRegisters _registers;
    private readonly TimeProvider _clock;
    private readonly SyncActivity? _activity;
    private HybridTimestamp? _last;
    private bool _loaded;

    public OperationLog(
        ISyncSettingsStore settings,
        ISyncOperationRepository operations,
        SyncRegisters registers,
        TimeProvider clock,
        SyncActivity? activity = null)
    {
        _activity = activity;
        _settings = settings;
        _operations = operations;
        _registers = registers;
        _clock = clock;
    }

    public async Task AppendAsync(IReadOnlyList<SyncOperationBody> operations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0) return;

        var settings = _settings.Load();
        if (settings is null) return;

        if (!_loaded)
        {
            _last = await _operations.GetLatestTimestampAsync(cancellationToken);
            _loaded = true;
        }

        foreach (var operation in operations)
        {
            var body = await _registers.WithBaseAsync(operation, cancellationToken);
            var timestamp = HybridClock.Tick(_last, _clock.GetUtcNow().ToUnixTimeMilliseconds(), settings.DeviceId);
            _last = timestamp;

            var (type, payload) = OperationCodec.Serialize(body);
            await _operations.AddAsync(new SyncOperation
            {
                HlcPhysicalMs = timestamp.PhysicalMs,
                HlcCounter = timestamp.Counter,
                DeviceId = timestamp.DeviceId,
                Generation = settings.Generation,
                Type = type,
                SchemaVersion = OperationCodec.SchemaVersionOf(body),
                MedicineId = body.MedicineId,
                EntityId = Operations.EntityOf(body),
                Payload = payload,
            }, cancellationToken);
            await _registers.RecordAsync(body.MedicineId, body, timestamp, cancellationToken);
        }
        // Raised before the caller saves: the listener only schedules a
        // run a few seconds later.
        _activity?.RaiseLocalOperationsRecorded();
    }
}
