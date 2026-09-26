using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;

namespace MedReminder.Prototypes.Sync.Tests;

// Runs every user action twice: on the real Application use cases over
// in-memory repositories (the oracle, today's behavior) and as
// replicated operations on a prototype replica. Only actions the oracle
// accepts are mirrored. Compare() asserts that the derived ledger gives
// the same stock and StockEpoch as the oracle ledger.
internal sealed class ParityHarness
{
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Guid _device = Guid.NewGuid();
    private readonly HlcClock _hlc;
    private readonly List<Guid> _medicines = new();

    public ParityHarness()
    {
        Oracle = new ApplicationTestScope(Origin);
        _hlc = new HlcClock(_device, () => Oracle.Clock.GetUtcNow().ToUnixTimeMilliseconds());
    }

    public ApplicationTestScope Oracle { get; }

    public Replica Proto { get; private set; } = new();

    public IReadOnlyList<Guid> Medicines => _medicines;

    public DateOnly Today => DateOnly.FromDateTime(Oracle.Clock.GetUtcNow().UtcDateTime);

    public List<string> Trace { get; } = new();

    // The hosted service runs the catch-up at startup; the harness does
    // it once at the start of every simulated day.
    public void StartDay(int dayIndex)
    {
        Oracle.Clock.SetUtcNow(Origin.AddDays(dayIndex).AddMinutes(5));
        Oracle.ConsumptionCatchUp.RunAsync(default).GetAwaiter().GetResult();
    }

    public void AdvanceTo(TimeSpan timeOfDay)
    {
        var target = new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) + timeOfDay;
        if (target > Oracle.Clock.GetUtcNow()) Oracle.Clock.SetUtcNow(target);
        else Oracle.Clock.AdvanceBy(TimeSpan.FromSeconds(1));
    }

    private Hlc Next() => _hlc.Now();

    private void Emit(Operation op) => Proto.Apply(op);

    private bool Try(string label, Action action)
    {
        try
        {
            action();
            Trace.Add(label);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public void AddMedicine(DateOnly start, decimal dose, int admins, decimal initial, int threshold,
        DateOnly? end, IReadOnlyList<SlotValue>? slots)
    {
        Guid id = default;
        var cmd = new AddMedicineCommand(
            "m", "tablet", dose, admins, start, threshold, NotificationChannels.Windows,
            EndDate: end, InitialQuantity: initial,
            AdministrationSlots: slots?.Select(s => new AdministrationSlotInput(s.Dose, s.Time, null)).ToList());
        if (!Try($"add {start} {dose}x{admins} init={initial} end={end} slots={slots?.Count}",
                () => id = Oracle.AddMedicine.ExecuteAsync(cmd, default).GetAwaiter().GetResult()))
            return;

        _medicines.Add(id);
        Emit(new MedicineCreated(Guid.NewGuid(), Next(), id, start, "tablet"));
        Emit(new MedicineFieldSet(Guid.NewGuid(), Next(), id, MedicineField.ThresholdDays, threshold.ToString()));
        if (end is { } e) Emit(new MedicineFieldSet(Guid.NewGuid(), Next(), id, MedicineField.EndDate, e.ToString("O")));
        Emit(new ScheduleRowAdded(Guid.NewGuid(), Next(), id, start, dose, admins));
        if (slots is { Count: > 0 }) Emit(new SlotsSet(Guid.NewGuid(), Next(), id, DateOnly.MinValue, slots));
        if (initial > 0m)
        {
            var at = new DateTimeOffset(start.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            Emit(new StockEntryAdded(Guid.NewGuid(), Next(), id, Guid.NewGuid(), StockMovementKind.InitialLoad, initial, at));
        }
    }

    public void AddStock(Guid id, decimal qty, StockMovementKind kind)
    {
        if (!Try($"addstock {kind} {qty}",
                () => Oracle.AddStock.ExecuteAsync(new AddStockCommand(id, qty, kind), default).GetAwaiter().GetResult()))
            return;
        Emit(new StockEntryAdded(Guid.NewGuid(), Next(), id, Guid.NewGuid(), kind, qty, Oracle.Clock.GetUtcNow()));
    }

    public void AdjustDown(Guid id, decimal qty)
    {
        if (!Try($"down {qty}",
                () => Oracle.AdjustStockDown.ExecuteAsync(new AdjustStockDownCommand(id, qty), default).GetAwaiter().GetResult()))
            return;
        Emit(new StockEntryAdded(Guid.NewGuid(), Next(), id, Guid.NewGuid(), StockMovementKind.NegativeCorrection, -qty, Oracle.Clock.GetUtcNow()));
    }

    public void Intake(Guid id, DateOnly day, IntakeStatus status, decimal qty)
    {
        Guid intakeId = default;
        if (!Try($"intake {day} {status} {qty}",
                () => intakeId = Oracle.RegisterIntake.ExecuteAsync(new RegisterIntakeCommand(id, day, status, qty), default).GetAwaiter().GetResult()))
            return;
        Emit(new IntakeRecorded(Guid.NewGuid(), Next(), id, intakeId, day, status, qty));
    }

    public void Count(Guid id, decimal counted, Func<decimal, decimal> pickTaken)
    {
        var snapshot = Oracle.ReconcileStock.LoadAsync(id, default).GetAwaiter().GetResult();
        var taken = pickTaken(snapshot.TodayScheduledQuantity);
        var threshold = Oracle.Medicines.GetAsync(id, default).GetAwaiter().GetResult()!.ThresholdDays;
        if (!Try($"count {counted} taken={taken}/{snapshot.TodayScheduledQuantity}",
                () => Oracle.ReconcileStock.ExecuteAsync(new ReconcileStockCommand(id, counted, taken), default).GetAwaiter().GetResult()))
            return;
        Emit(new StockCountRecorded(Guid.NewGuid(), Next(), id, Guid.NewGuid(), Today, counted, taken, threshold));
    }

    public void ChangeSchedule(Guid id, DateOnly from, decimal dose, int admins)
    {
        if (!Try($"schedule {from} {dose}x{admins}",
                () => Oracle.ChangeMedicationSchedule.ExecuteAsync(new ChangeMedicationScheduleCommand(id, dose, admins, from), default).GetAwaiter().GetResult()))
            return;
        Emit(new ScheduleRowAdded(Guid.NewGuid(), Next(), id, from, dose, admins));
    }

    public void Suspend(Guid id, DateOnly start)
    {
        Guid suspensionId = default;
        if (!Try($"suspend {start}",
                () => suspensionId = Oracle.SuspendMedication.ExecuteAsync(new SuspendMedicationCommand(id, start), default).GetAwaiter().GetResult()))
            return;
        Emit(new SuspensionCreated(Guid.NewGuid(), Next(), id, suspensionId, start));
    }

    public void Resume(Guid id, DateOnly end)
    {
        var open = Oracle.Suspensions.GetOpenSuspensionAsync(id, default).GetAwaiter().GetResult();
        if (open is null) return;
        var suspensionId = open.Id;
        if (!Try($"resume {end}",
                () => Oracle.ResumeMedication.ExecuteAsync(new ResumeMedicationCommand(id, end), default).GetAwaiter().GetResult()))
            return;
        Emit(new SuspensionEndSet(Guid.NewGuid(), Next(), id, suspensionId, end));
    }

    // UpdateMedicine writes every field; the prototype emits only the
    // fields the user changed (§7.4, stale forms).
    public void Update(Guid id, int? threshold = null, bool setEnd = false, DateOnly? end = null,
        bool? active = null, IReadOnlyList<SlotValue>? slots = null)
    {
        var m = Oracle.Medicines.GetAsync(id, default).GetAwaiter().GetResult()!;
        var cmd = new UpdateMedicineCommand(
            id, m.Name, m.ActiveIngredient, m.Package, m.Unit,
            threshold ?? m.ThresholdDays, m.NotificationChannels,
            setEnd ? end : m.EndDate, m.DoctorName, m.Notes,
            active ?? m.IsActive, m.RemindOnDose,
            slots?.Select(s => new AdministrationSlotInput(s.Dose, s.Time, null)).ToList());
        if (!Try($"update thr={threshold} end={(setEnd ? end : "-")} active={active} slots={slots?.Count}",
                () => Oracle.UpdateMedicine.ExecuteAsync(cmd, default).GetAwaiter().GetResult()))
            return;
        if (threshold is { } t) Emit(new MedicineFieldSet(Guid.NewGuid(), Next(), id, MedicineField.ThresholdDays, t.ToString()));
        if (setEnd) Emit(new MedicineFieldSet(Guid.NewGuid(), Next(), id, MedicineField.EndDate, end?.ToString("O")));
        if (active is { } a) Emit(new ActivitySet(Guid.NewGuid(), Next(), id, a, Today));
        if (slots is not null) Emit(new SlotsSet(Guid.NewGuid(), Next(), id, Today, slots));
    }

    // Phase 2 boot patch (§3.5): freeze the oracle's ledger as Legacy
    // facts with cutoff = yesterday, and rebuild the replica from them.
    public void ApplyCutoffPatch()
    {
        var genesis = Hlc.Zero with { PhysicalMs = 1 };
        var fresh = new Replica();
        var cutoff = Today.AddDays(-1);
        foreach (var id in _medicines)
        {
            var m = Oracle.Medicines.GetAsync(id, default).GetAwaiter().GetResult()!;
            // Carry the non-ledger replicated facts (schedule, slots,
            // suspensions, fields, activity) as they are.
            foreach (var op in Proto.Log.Where(o => o.MedicineId == id
                         && o is not StockEntryAdded and not StockCountRecorded and not IntakeRecorded and not IntakeStatusSet))
                fresh.Apply(op);
            foreach (var mv in Oracle.Stock.ListForMedicineAsync(id, default).GetAwaiter().GetResult())
                fresh.Apply(new LegacyMovement(Guid.NewGuid(), genesis, id, mv.Id, mv.Kind, mv.QuantityDelta, mv.OccurredAt));
            foreach (var i in Oracle.Intakes.ListForMedicineAsync(id, default).GetAwaiter().GetResult())
                fresh.Apply(new LegacyIntake(Guid.NewGuid(), genesis, id, i.Id, i.Day, i.Status, i.Quantity));
            fresh.Apply(new LegacyBaseline(Guid.NewGuid(), genesis, id, cutoff, m.StockEpoch));
        }
        Proto = fresh;
        Trace.Add($"patch cutoff={cutoff}");
    }

    // MedicationMonitor runs the catch-up every 30 minutes; comparing
    // after a tick removes the timing difference between the two.
    public void Compare()
    {
        Oracle.ConsumptionCatchUp.RunAsync(default).GetAwaiter().GetResult();
        foreach (var id in _medicines)
        {
            var ledger = Oracle.Stock.ListForMedicineAsync(id, default).GetAwaiter().GetResult();
            var oracleStock = MedicineStock.Current(ledger);
            var oracleEpoch = Oracle.Medicines.GetAsync(id, default).GetAwaiter().GetResult()!.StockEpoch;
            var derived = LedgerDeriver.Derive(Proto, id, Today);
            if (derived.Stock != oracleStock || derived.Epoch != oracleEpoch)
            {
                throw new ParityException(
                    $"Medicine {id} on {Today}: oracle stock {oracleStock} epoch {oracleEpoch}, " +
                    $"derived stock {derived.Stock} epoch {derived.Epoch} (raw oracle {ledger.Sum(x => x.QuantityDelta)}, raw derived {derived.RawTotal})\n" +
                    string.Join("\n", Trace.TakeLast(40)) +
                    "\nORACLE:\n" + string.Join("\n", ledger.OrderBy(x => x.OccurredAt).Select(x => $"  {x.OccurredAt:yyyy-MM-dd HH:mm} {x.Kind} {x.QuantityDelta}")) +
                    "\nDERIVED:\n" + string.Join("\n", derived.Rows.OrderBy(x => x.Day).Select(x => $"  {x.Day} {x.Rule} {x.Kind} {x.Delta}")));
            }
        }
    }
}

internal sealed class ParityException(string message) : Exception(message);
