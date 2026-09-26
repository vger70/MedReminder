using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Prototypes.Sync.Simulation;

public sealed record SimulationResult(
    int Seed,
    int Devices,
    int Operations,
    int Medicines,
    int Bootstraps,
    int SegmentsDeleted,
    int DuplicatesIgnored,
    int ScheduleConflicts,
    IReadOnlyList<string> Failures);

// S9 acceptance (b): devices edit the same profile offline and in
// parallel, deliver in random order with duplicates, go offline for
// weeks, compact, join late. After full delivery every device must hold
// the same replicated state, equal to the HLC-ordered fold of all
// operations, and the same derived ledger (§11).
public sealed class SyncSimulation
{
    private const long DayMs = 24L * 60 * 60 * 1000;
    private static readonly DateOnly Day0 = new(2026, 3, 1);

    private readonly Random _rng;
    private readonly SimCloud _cloud = new();
    private readonly List<SimDevice> _devices = new();
    private readonly Dictionary<SimDevice, long> _skewMs = new();
    private readonly Dictionary<SimDevice, int> _offlineUntilDay = new();
    private readonly List<Operation> _allOps = new();
    private readonly int _seed;
    private long _globalMs;
    private int _checkpoints;

    public SyncSimulation(int seed)
    {
        _seed = seed;
        _rng = new Random(seed);
    }

    private DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(_globalMs).UtcDateTime);

    public SimulationResult Run(int days = 30)
    {
        _globalMs = new DateTimeOffset(Day0.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds();
        var deviceCount = _rng.Next(2, 6);
        for (var i = 0; i < deviceCount; i++) AddDevice($"d{i}");
        var lateJoinDay = _rng.NextDouble() < 0.5 ? _rng.Next(5, days - 5) : -1;
        const long staleAfterMs = 10 * DayMs;

        for (var day = 0; day < days; day++)
        {
            var dayStart = new DateTimeOffset(Day0.AddDays(day).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds();
            if (day == lateJoinDay)
            {
                var joiner = AddDevice($"late{day}");
                if (_cloud.Checkpoints.Count > 0) joiner.Bootstrap(_cloud);
            }

            foreach (var d in _devices)
            {
                if (_offlineUntilDay.TryGetValue(d, out var until) && until <= day) { d.Online = true; _offlineUntilDay.Remove(d); }
                if (d.Online && _rng.NextDouble() < 0.07)
                {
                    d.Online = false;
                    _offlineUntilDay[d] = day + (_rng.NextDouble() < 0.3 ? _rng.Next(11, 20) : _rng.Next(1, 5));
                }
            }

            var events = new List<(long At, SimDevice Device, bool Sync)>();
            foreach (var d in _devices)
            {
                for (var k = _rng.Next(0, 5); k > 0; k--) events.Add((dayStart + _rng.Next(6, 23) * 3_600_000L + _rng.Next(0, 3_600_000), d, false));
                for (var k = _rng.Next(0, 3); k > 0; k--) events.Add((dayStart + _rng.Next(0, 24) * 3_600_000L, d, true));
            }

            foreach (var (at, device, sync) in events.OrderBy(e => e.At))
            {
                _globalMs = at;
                if (sync)
                {
                    if (!device.Online) continue;
                    device.Seal(_cloud);
                    device.Pull(_cloud, _rng, _globalMs);
                    if (_rng.NextDouble() < 0.05)
                    {
                        device.WriteCheckpoint(_cloud, ++_checkpoints);
                        _cloud.CollectGarbage(_globalMs, staleAfterMs);
                    }
                }
                else
                {
                    LocalAction(device);
                }
            }
        }

        // Everyone online, full delivery until quiescent.
        foreach (var d in _devices) d.Online = true;
        for (var round = 0; round < 50; round++)
        {
            var progress = false;
            foreach (var d in _devices) d.Seal(_cloud);
            foreach (var d in _devices) progress |= d.Pull(_cloud, _rng, _globalMs);
            if (!progress && round > 1) break;
        }

        return new SimulationResult(
            _seed, _devices.Count, _allOps.Count, _devices[0].Replica.Medicines.Count,
            _devices.Sum(d => d.Bootstraps), _cloud.SegmentsDeleted, _devices.Sum(d => d.DuplicatesIgnored),
            _devices.Max(d => d.Replica.ScheduleConflicts.Count), Check());
    }

    private SimDevice AddDevice(string name)
    {
        SimDevice? device = null;
        device = new SimDevice(name, () => _globalMs + _skewMs[device!]);
        _skewMs[device] = _rng.NextDouble() < 0.1 ? _rng.Next(1, 3) * 3_600_000L : _rng.Next(-300_000, 300_000);
        _devices.Add(device);
        return device;
    }

    private List<string> Check()
    {
        var failures = new List<string>();
        var reference = Replica.FoldSorted(_allOps);
        var refHash = reference.StateHash();
        var today = Today.AddDays(1);

        foreach (var d in _devices)
        {
            if (d.Replica.Log.Count != _allOps.Count)
                failures.Add($"{d.Name}: {d.Replica.Log.Count} operations applied, {_allOps.Count} produced (R3)");
            if (d.Replica.StateHash() != refHash)
                failures.Add($"{d.Name}: replicated state differs from the HLC-ordered fold (R2)");
        }

        foreach (var medicineId in reference.Medicines.Keys)
        {
            if (reference.Medicines[medicineId].Created is null) continue;
            var expected = LedgerDeriver.Derive(reference, medicineId, today);
            failures.AddRange(Invariants(reference, medicineId, expected, today));
            foreach (var d in _devices)
            {
                var derived = LedgerDeriver.Derive(d.Replica, medicineId, today);
                if (derived.RawTotal != expected.RawTotal || derived.Epoch != expected.Epoch || derived.Rows.Count != expected.Rows.Count)
                    failures.Add($"{d.Name}: derived ledger of {medicineId} differs (R6)");
            }
        }
        return failures;
    }

    // ANALYSIS.md §4.4 invariants restated for the derived ledger.
    private static IEnumerable<string> Invariants(Replica r, Guid id, DerivedLedger ledger, DateOnly today)
    {
        var view = new MedicineView(r, r.Medicines[id], asOf: null);
        var auto = ledger.Rows.Where(x => x.Rule == "auto").ToList();
        var consumptionDays = ledger.Rows.Where(x => x.Rule is "auto" or "count-day").GroupBy(x => x.Day);
        foreach (var g in consumptionDays)
            if (g.Count() > 1) yield return $"{id}: more than one automatic consumption on {g.Key}";
        var intakeDays = view.Intakes().Select(i => i.Intake.Day).ToHashSet();
        var suspensions = view.Suspensions();
        foreach (var row in auto)
        {
            if (row.Day <= ledger.CutoffDay) yield return $"{id}: automatic consumption on frozen day {row.Day}";
            if (row.Day >= today) yield return $"{id}: automatic consumption for today or later ({row.Day})";
            if (row.Day < view.StartDate) yield return $"{id}: consumption before start ({row.Day})";
            if (view.EndDate is { } end && row.Day > end) yield return $"{id}: consumption after end ({row.Day})";
            if (intakeDays.Contains(row.Day)) yield return $"{id}: automatic consumption on an intake day ({row.Day})";
            if (MedReminder.Domain.Calculations.SuspensionState.IsSuspendedOn(row.Day, suspensions)) yield return $"{id}: consumption on a suspended day ({row.Day})";
            if (!view.IsActiveOn(row.Day)) yield return $"{id}: consumption on an inactive day ({row.Day})";
        }
        if (ledger.Epoch < 1) yield return $"{id}: epoch below 1";
    }

    private void Emit(SimDevice d, Operation op)
    {
        d.Local(op);
        _allOps.Add(op);
    }

    private void LocalAction(SimDevice d)
    {
        var today = Today;
        var known = d.Replica.Medicines.Values.Where(m => m.Created is not null).ToList();
        if (known.Count == 0 || _rng.NextDouble() < 0.06)
        {
            CreateMedicine(d, today);
            return;
        }
        var m = known[_rng.Next(known.Count)];
        var id = m.Id;
        switch (_rng.Next(0, 16))
        {
            case 0:
                Emit(d, new MedicineFieldSet(Guid.NewGuid(), d.Next(), id, MedicineField.ThresholdDays, _rng.Next(0, 15).ToString()));
                break;
            case 1:
                Emit(d, new MedicineFieldSet(Guid.NewGuid(), d.Next(), id, MedicineField.EndDate,
                    _rng.NextDouble() < 0.3 ? null : today.AddDays(_rng.Next(-5, 20)).ToString("O")));
                break;
            case 2:
                Emit(d, new ActivitySet(Guid.NewGuid(), d.Next(), id, _rng.NextDouble() < 0.5, today));
                break;
            case 3:
                Emit(d, new SlotsSet(Guid.NewGuid(), d.Next(), id, today, RandomSlots()));
                break;
            case 4:
                Emit(d, new ScheduleRowAdded(Guid.NewGuid(), d.Next(), id,
                    today.AddDays(_rng.Next(-5, 6)), _rng.Next(1, 4) * 0.5m, _rng.Next(1, 4)));
                break;
            case 5:
                Emit(d, new SuspensionCreated(Guid.NewGuid(), d.Next(), id, Guid.NewGuid(), today.AddDays(_rng.Next(-3, 4))));
                break;
            case 6:
                var susp = m.Suspensions.Where(s => s.Value.CreatedHlc is not null).Select(s => s.Key).ToList();
                if (susp.Count > 0)
                    Emit(d, new SuspensionEndSet(Guid.NewGuid(), d.Next(), id, susp[_rng.Next(susp.Count)], today.AddDays(_rng.Next(-1, 5))));
                break;
            case 7:
            case 8:
                var kinds = new[] { StockMovementKind.NewPackage, StockMovementKind.ManualAdd, StockMovementKind.PositiveCorrection };
                Emit(d, new StockEntryAdded(Guid.NewGuid(), d.Next(), id, Guid.NewGuid(), kinds[_rng.Next(3)], _rng.Next(1, 31), DateTimeOffset.FromUnixTimeMilliseconds(_globalMs)));
                break;
            case 9:
                var down = _rng.Next(1, 11);
                if (LedgerDeriver.Derive(d.Replica, id, today).Stock >= down)
                    Emit(d, new StockEntryAdded(Guid.NewGuid(), d.Next(), id, Guid.NewGuid(), StockMovementKind.NegativeCorrection, -down, DateTimeOffset.FromUnixTimeMilliseconds(_globalMs)));
                break;
            case 10:
            case 11:
                var status = Enum.GetValues<IntakeStatus>()[_rng.Next(4)];
                var qty = _rng.Next(1, 4);
                if (status != IntakeStatus.Taken || LedgerDeriver.Derive(d.Replica, id, today).Stock >= qty)
                    Emit(d, new IntakeRecorded(Guid.NewGuid(), d.Next(), id, Guid.NewGuid(), today.AddDays(-_rng.Next(0, 4)), status, qty));
                break;
            case 12:
                var intakes = m.Intakes.Where(i => i.Value.Recorded is not null).Select(i => i.Key).ToList();
                if (intakes.Count > 0)
                    Emit(d, new IntakeStatusSet(Guid.NewGuid(), d.Next(), id, intakes[_rng.Next(intakes.Count)], Enum.GetValues<IntakeStatus>()[_rng.Next(4)]));
                break;
            case 13:
            case 14:
                var ledger = LedgerDeriver.Derive(d.Replica, id, today);
                var scheduled = LedgerDeriver.TodayScheduled(d.Replica, id, today, ledger.Anchors);
                var taken = _rng.Next(3) switch { 0 => 0m, 1 => scheduled, _ => scheduled / 2m };
                var threshold = m.Fields.TryGetValue(MedicineField.ThresholdDays, out var t) && int.TryParse(t.Current, out var tv) ? tv : 5;
                Emit(d, new StockCountRecorded(Guid.NewGuid(), d.Next(), id, Guid.NewGuid(), today, _rng.Next(0, 61), taken, threshold));
                break;
            case 15:
                var facts = m.Entries.Keys.Concat(m.Counts.Keys).Concat(m.Intakes.Keys).Concat(m.Suspensions.Keys).ToList();
                if (facts.Count > 0)
                    Emit(d, new FactRetracted(Guid.NewGuid(), d.Next(), id, facts[_rng.Next(facts.Count)]));
                break;
        }
    }

    private void CreateMedicine(SimDevice d, DateOnly today)
    {
        var id = Guid.NewGuid();
        var start = today.AddDays(_rng.Next(-10, 3));
        Emit(d, new MedicineCreated(Guid.NewGuid(), d.Next(), id, start, "tablet"));
        Emit(d, new MedicineFieldSet(Guid.NewGuid(), d.Next(), id, MedicineField.ThresholdDays, _rng.Next(3, 11).ToString()));
        Emit(d, new ScheduleRowAdded(Guid.NewGuid(), d.Next(), id, start, _rng.Next(1, 4) * 0.5m, _rng.Next(1, 4)));
        if (_rng.NextDouble() < 0.3) Emit(d, new SlotsSet(Guid.NewGuid(), d.Next(), id, DateOnly.MinValue, RandomSlots()));
        var initial = _rng.Next(0, 61);
        if (initial > 0)
            Emit(d, new StockEntryAdded(Guid.NewGuid(), d.Next(), id, Guid.NewGuid(), StockMovementKind.InitialLoad, initial,
                new DateTimeOffset(start.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)));
    }

    private List<SlotValue> RandomSlots()
        => _rng.NextDouble() < 0.2
            ? []
            : Enumerable.Range(0, _rng.Next(1, 4)).Select(i => new SlotValue(_rng.Next(1, 3) * 0.5m, new TimeOnly(8 + i * 5, 0))).ToList();
}
