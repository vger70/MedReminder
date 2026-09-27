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
public sealed class OperationLog : IOperationLog
{
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncOperationRepository _operations;
    private readonly TimeProvider _clock;
    private HybridTimestamp? _last;
    private bool _loaded;

    public OperationLog(
        ISyncSettingsStore settings,
        ISyncOperationRepository operations,
        TimeProvider clock)
    {
        _settings = settings;
        _operations = operations;
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

        foreach (var body in operations)
        {
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
                SchemaVersion = OperationCodec.CurrentSchemaVersion,
                MedicineId = body.MedicineId,
                Payload = payload,
            }, cancellationToken);
        }
    }
}
