using MedReminder.Application.Ledger;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Tests.Ledger;

// Port of the S9 parity harness (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §18.9). In Phase 2c-1 it proved that LedgerDeriver reproduces the
// pre-derivation use cases. Since Phase 2c-2 the use cases derive their
// ledger themselves (LedgerSynchronizer), so the harness now checks the
// wiring: it records every fact on its own (intake instants, activity
// changes, count outcomes, the freeze) and Compare() checks that the
// ledger the application stored equals a fresh derivation from those
// facts: same derived rows, stock and StockEpoch.
internal sealed class LedgerParityHarness
{
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    private readonly List<Guid> _medicines = new();
    private readonly Dictionary<Guid, DateTimeOffset> _intakeRecordedAt = new();
    private readonly Dictionary<Guid, List<LedgerActivity>> _activity = new();
    private readonly Dictionary<Guid, List<StockCountAnchor>> _counts = new();

    // Freeze (§3.5), applied by ApplyCutoffPatch.
    private DateOnly? _cutoff;
    private readonly HashSet<Guid> _frozenMedicines = new();
    private readonly HashSet<Guid> _frozenMovements = new();
    private readonly HashSet<Guid> _frozenIntakes = new();
    private readonly Dictionary<Guid, int> _baselineEpoch = new();

    public LedgerParityHarness()
    {
        App = new ApplicationTestScope(Origin);
    }

    public ApplicationTestScope App { get; }

    public IReadOnlyList<Guid> Medicines => _medicines;

    public DateOnly Today => DateOnly.FromDateTime(App.Clock.GetUtcNow().UtcDateTime);

    public List<string> Trace { get; } = new();

    private DateTimeOffset Now => App.Clock.GetUtcNow();

    // The hosted service runs the catch-up at startup; the harness does
    // it once at the start of every simulated day.
    public void StartDay(int dayIndex)
    {
        App.Clock.SetUtcNow(Origin.AddDays(dayIndex).AddMinutes(5));
        App.ConsumptionCatchUp.RunAsync(default).GetAwaiter().GetResult();
    }

    public void AdvanceTo(TimeSpan timeOfDay)
    {
        var target = new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) + timeOfDay;
        if (target > Now) App.Clock.SetUtcNow(target);
        else App.Clock.AdvanceBy(TimeSpan.FromSeconds(1));
    }

    private bool Try(string label, Action action)
    {
        try
        {
            action();
            Trace.Add($"{Now:MM-dd HH:mm} {label}");
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static T Run<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private static void Run(Task task) => task.GetAwaiter().GetResult();

    public void AddMedicine(DateOnly start, decimal dose, int admins, decimal initial, int threshold,
        DateOnly? end, IReadOnlyList<AdministrationSlotInput>? slots)
    {
        Guid id = default;
        var cmd = new AddMedicineCommand(
            "m", "tablet", dose, admins, start, threshold, NotificationChannels.Windows,
            EndDate: end, InitialQuantity: initial, AdministrationSlots: slots);
        if (!Try($"add {start} {dose}x{admins} init={initial} end={end} slots={slots?.Count}",
                () => id = Run(App.AddMedicine.ExecuteAsync(cmd, default))))
            return;
        _medicines.Add(id);
        _activity[id] = new List<LedgerActivity>();
        _counts[id] = new List<StockCountAnchor>();
    }

    public void AddStock(Guid id, decimal qty, StockMovementKind kind)
        => Try($"addstock {kind} {qty}",
            () => Run(App.AddStock.ExecuteAsync(new AddStockCommand(id, qty, kind), default)));

    public void AdjustDown(Guid id, decimal qty)
        => Try($"down {qty}",
            () => Run(App.AdjustStockDown.ExecuteAsync(new AdjustStockDownCommand(id, qty), default)));

    public void Intake(Guid id, DateOnly day, IntakeStatus status, decimal qty)
    {
        Guid intakeId = default;
        if (Try($"intake {day} {status} {qty}",
                () => intakeId = Run(App.RegisterIntake.ExecuteAsync(new RegisterIntakeCommand(id, day, status, qty), default))))
        {
            _intakeRecordedAt[intakeId] = Now;
        }
    }

    // Evaluates the count on the facts recorded so far (what Phase 2c
    // stores with the StockCount fact) and checks it against the
    // oracle's ReconcileStock.
    public void Count(Guid id, decimal counted, Func<decimal, decimal> pickTaken)
    {
        var snapshot = Run(App.ReconcileStock.LoadAsync(id, default));
        var facts = Facts(id);
        var scheduled = LedgerDeriver.CountDayScheduled(facts, Today, Zone);
        if (scheduled != snapshot.TodayScheduledQuantity)
        {
            throw Mismatch(id, $"count-day scheduled: oracle {snapshot.TodayScheduledQuantity}, derived {scheduled}");
        }

        var taken = pickTaken(snapshot.TodayScheduledQuantity);
        var threshold = Run(App.Medicines.GetAsync(id, default))!.ThresholdDays;
        ReconcileStockResult? result = null;
        if (!Try($"count {counted} taken={taken}/{snapshot.TodayScheduledQuantity}",
                () => result = Run(App.ReconcileStock.ExecuteAsync(new ReconcileStockCommand(id, counted, taken), default))))
            return;

        // The count fact the application stored, and the outcome the
        // harness evaluates on its own facts: they must agree.
        var stored = App.Counts.All.Last(c => c.MedicineId == id);
        var anchor = LedgerDeriver.EvaluateCount(facts, stored.Id, Today, Now, counted, taken, threshold, Zone);
        if (anchor.Correction != result!.Correction || anchor.AdvancesEpoch != result.StockEpochAdvanced
            || anchor.Correction != stored.Correction || anchor.RecordedAt != stored.RecordedAt
            || anchor.LedgerAtStartOfDay != stored.LedgerAtStartOfDay
            || anchor.CountDayScheduled != stored.CountDayScheduled
            || anchor.MaterializesCountDay != stored.MaterializesCountDay
            || anchor.AdvancesEpoch != stored.AdvancesEpoch)
        {
            throw Mismatch(id,
                $"count: result correction {result.Correction} epoch+ {result.StockEpochAdvanced}, " +
                $"stored {stored.Correction} / {stored.AdvancesEpoch}, " +
                $"derived correction {anchor.Correction} epoch+ {anchor.AdvancesEpoch}");
        }
        _counts[id].Add(anchor);
    }

    public void ChangeSchedule(Guid id, DateOnly from, decimal dose, int admins)
        => Try($"schedule {from} {dose}x{admins}",
            () => Run(App.ChangeMedicationSchedule.ExecuteAsync(
                new ChangeMedicationScheduleCommand(id, dose, admins, from), default)));

    public void Suspend(Guid id, DateOnly start)
        => Try($"suspend {start}",
            () => Run(App.SuspendMedication.ExecuteAsync(new SuspendMedicationCommand(id, start), default)));

    public void Resume(Guid id, DateOnly end)
        => Try($"resume {end}",
            () => Run(App.ResumeMedication.ExecuteAsync(new ResumeMedicationCommand(id, end), default)));

    // Retracts a random retractable fact (B.1 Phase 2d) and mirrors it
    // in the harness's own facts: intakes, stock entries and
    // suspensions are read back from the store; count outcomes are
    // tracked here.
    public void RetractRandom(Guid id, Random rng)
    {
        var candidates = Run(App.FactHistory.LoadAsync(id, default)).Where(i => i.CanRetract).ToList();
        if (candidates.Count == 0) return;
        var item = candidates[rng.Next(candidates.Count)];
        if (Try($"retract {item.Kind} {item.Day}",
                () => Run(App.RetractFact.ExecuteAsync(new RetractFactCommand(id, item.Kind, item.FactId), default))))
        {
            _counts[id].RemoveAll(c => c.Id == item.FactId);
        }
    }

    public void Deactivate(Guid id)
    {
        if (Try("deactivate",
                () => Run(App.DeactivateMedicine.ExecuteAsync(new DeactivateMedicineCommand(id), default))))
        {
            _activity[id].Add(new LedgerActivity(Today, false, Now));
        }
    }

    public void Update(Guid id, int? threshold = null, bool setEnd = false, DateOnly? end = null,
        bool? active = null, IReadOnlyList<AdministrationSlotInput>? slots = null)
    {
        var m = Run(App.Medicines.GetAsync(id, default))!;
        // The fake returns the stored instance, which the use case
        // mutates: capture the value before.
        var wasActive = m.IsActive;
        var cmd = new UpdateMedicineCommand(
            id, m.Name, m.ActiveIngredient, m.Package, m.Unit,
            threshold ?? m.ThresholdDays, m.NotificationChannels,
            setEnd ? end : m.EndDate, m.DoctorName, m.Notes,
            active ?? m.IsActive, m.RemindOnDose, slots);
        if (!Try($"update thr={threshold} end={(setEnd ? end : "-")} active={active} slots={slots?.Count}",
                () => Run(App.UpdateMedicine.ExecuteAsync(cmd, default))))
            return;
        if (active is { } a && a != wasActive)
        {
            _activity[id].Add(new LedgerActivity(Today, a, Now));
        }
    }

    // Boot patch / import freeze (§3.5, LedgerFreeze): applied to the
    // application's store, and mirrored in the harness's own facts:
    // every movement and intake recorded so far becomes Legacy, cutoff =
    // yesterday, epoch baseline = the medicine's current StockEpoch.
    // Counts recorded before it are only their Legacy rows from then on.
    public void ApplyCutoffPatch()
    {
        App.FreezeLedger();
        _cutoff = Today.AddDays(-1);
        foreach (var id in _medicines)
        {
            _frozenMedicines.Add(id);
            _baselineEpoch[id] = Run(App.Medicines.GetAsync(id, default))!.StockEpoch;
            _counts[id].Clear();
        }
        foreach (var m in App.Stock.All) _frozenMovements.Add(m.Id);
        foreach (var i in App.Intakes.All) _frozenIntakes.Add(i.Id);
        Trace.Add($"patch cutoff={_cutoff}");
    }

    public LedgerFacts Facts(Guid id)
    {
        var m = Run(App.Medicines.GetAsync(id, default))!;
        var frozen = _frozenMedicines.Contains(id);
        var movements = App.Stock.All.Where(x => x.MedicineId == id).ToList();
        var sets = App.Slots.Sets.Where(s => s.MedicineId == id).ToList();

        return new LedgerFacts
        {
            MedicineId = id,
            StartDate = m.StartDate,
            EndDate = m.EndDate,
            CutoffDay = frozen ? _cutoff!.Value : m.StartDate.AddDays(-1),
            BaselineEpoch = frozen ? _baselineEpoch[id] : 1,
            LegacyMovements = movements.Where(x => _frozenMovements.Contains(x.Id)).ToList(),
            UserEntries = movements
                .Where(x => !_frozenMovements.Contains(x.Id) && x.Origin == StockMovementOrigin.User)
                .ToList(),
            Intakes = App.Intakes.All
                .Where(i => i.MedicineId == id)
                .Select(i => new LedgerIntake(
                    i.Id, i.Day, i.Status, i.Quantity, _intakeRecordedAt[i.Id], _frozenIntakes.Contains(i.Id)))
                .ToList(),
            Schedule = Run(App.Schedules.ListForMedicineAsync(id, default)),
            Suspensions = Run(App.Suspensions.ListForMedicineAsync(id, default)),
            SlotSets = sets
                .Select(s => new LedgerSlotSet(
                    s.EffectiveFrom, s.RecordedAt,
                    App.Slots.All.Where(x => x.SetId == s.Id).OrderBy(x => x.Order).ToList()))
                .ToList(),
            Activity = _activity[id],
            Counts = _counts[id],
        };
    }

    public DerivedLedger Derive(Guid id) => LedgerDeriver.Derive(Facts(id), Today, Zone);

    // MedicationMonitor runs the catch-up every 30 minutes; comparing
    // after a tick brings the stored ledger up to today.
    public void Compare()
    {
        Run(App.ConsumptionCatchUp.RunAsync(default));
        foreach (var id in _medicines)
        {
            var ledger = Run(App.Stock.ListForMedicineAsync(id, default));
            var oracleStock = MedicineStock.Current(ledger);
            var oracleEpoch = Run(App.Medicines.GetAsync(id, default))!.StockEpoch;
            var derived = Derive(id);
            var storedRows = ledger.Where(m => m.Origin == StockMovementOrigin.Derived)
                .Select(m => (m.Id, m.Kind, m.QuantityDelta)).OrderBy(x => x.Id).ToList();
            var derivedRows = derived.DerivedRows
                .Select(r => (r.Id, r.Kind, r.Delta)).OrderBy(x => x.Id).ToList();
            if (derived.Stock != oracleStock || derived.Epoch != oracleEpoch || !storedRows.SequenceEqual(derivedRows))
            {
                throw Mismatch(id,
                    $"oracle stock {oracleStock} epoch {oracleEpoch}, derived stock {derived.Stock} epoch {derived.Epoch} " +
                    $"(raw oracle {ledger.Sum(x => x.QuantityDelta)}, raw derived {derived.RawTotal})\n" +
                    "ORACLE:\n" + string.Join("\n", ledger.OrderBy(x => x.OccurredAt)
                        .Select(x => $"  {x.OccurredAt:yyyy-MM-dd HH:mm} {x.Kind} {x.QuantityDelta}")) +
                    "\nDERIVED:\n" + string.Join("\n", derived.Rows.OrderBy(x => x.Day)
                        .Select(x => $"  {x.Day} {x.Rule} {x.Kind} {x.Delta}")));
            }
        }
    }

    private LedgerParityException Mismatch(Guid id, string detail)
        => new($"Medicine {id} on {Today}: {detail}\n" + string.Join("\n", Trace.TakeLast(40)));
}

internal sealed class LedgerParityException(string message) : Exception(message);
