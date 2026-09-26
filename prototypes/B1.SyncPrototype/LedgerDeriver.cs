using System.Globalization;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Prototypes.Sync;

public sealed record DerivedRow(string Rule, StockMovementKind Kind, decimal Delta, DateOnly Day);

public sealed record AnchorResult(
    Guid FactId,
    Hlc Hlc,
    DateOnly CountDay,
    decimal Correction,
    bool MaterializesCountDay,
    bool AdvancesEpoch,
    decimal TodayScheduled);

public sealed record DerivedLedger(
    IReadOnlyList<DerivedRow> Rows,
    decimal RawTotal,
    decimal Stock,
    int Epoch,
    DateOnly CutoffDay,
    IReadOnlyList<AnchorResult> Anchors);

// Read-only view of a medicine's replicated state "as of" an HLC
// (exclusive), or current when AsOf is null. Count anchors are evaluated
// on the view as of their own HLC (snapshot semantics, see the S9
// results in ANALYSIS-B1-MOBILE-SYNC.md §18).
public sealed class MedicineView
{
    private readonly MedicineState _m;
    private readonly Replica _replica;

    public MedicineView(Replica replica, MedicineState m, Hlc? asOf)
    {
        _replica = replica;
        _m = m;
        AsOf = asOf;
    }

    public Hlc? AsOf { get; }

    public bool Exists => _m.Created is { } c && Before(c.Hlc);

    public DateOnly StartDate => _m.Created!.StartDate;

    public DateOnly? EndDate
    {
        get
        {
            if (!_m.Fields.TryGetValue(MedicineField.EndDate, out var reg)) return null;
            return reg.TryGetAsOf(AsOf, out var v) && v is not null
                ? DateOnly.ParseExact(v, "O", CultureInfo.InvariantCulture)
                : null;
        }
    }

    public DateOnly CutoffDay => _m.Baseline?.CutoffDay ?? StartDate.AddDays(-1);

    public int BaselineEpoch => _m.Baseline?.Epoch ?? 1;

    public bool IsActiveOn(DateOnly day)
    {
        ActivitySet? latest = null;
        foreach (var a in _m.Activity.Values)
        {
            if (!Before(a.Hlc) || a.Day > day) continue;
            if (latest is null || a.Hlc > latest.Hlc) latest = a;
        }
        return latest?.Active ?? true;
    }

    public IReadOnlyList<MedicationAdministrationSlot> SlotsOn(DateOnly day)
    {
        // Slots carry no user-chosen date: a set recorded on day T
        // applies from T on. Among the sets already in force on `day`,
        // the most recently recorded one wins, so a change made today
        // also replaces a set whose start is still in the future.
        Hlc? best = null;
        IReadOnlyList<SlotValue>? value = null;
        foreach (var (from, reg) in _m.Slots)
        {
            if (from > day) continue;
            foreach (var (hlc, v) in reg.Versions)
            {
                if (AsOf is { } limit && hlc >= limit) break;
                if (best is { } b && hlc <= b) continue;
                best = hlc;
                value = v;
            }
        }
        if (value is null) return [];
        return value.Select((s, i) => new MedicationAdministrationSlot
        {
            MedicineId = _m.Id,
            Dose = s.Dose,
            Time = s.Time,
            Order = i,
        }).ToList();
    }

    public IReadOnlyList<MedicationScheduleHistory> ScheduleRows()
    {
        var rows = new List<MedicationScheduleHistory>();
        foreach (var (from, reg) in _m.Schedule.OrderBy(s => s.Key))
        {
            if (!reg.TryGetAsOf(AsOf, out var v)) continue;
            rows.Add(new MedicationScheduleHistory
            {
                MedicineId = _m.Id,
                EffectiveFrom = from,
                DosePerAdministration = v.Dose,
                AdministrationsPerDay = v.Administrations,
            });
        }
        return rows;
    }

    public IReadOnlyList<MedicationSuspension> Suspensions()
    {
        var list = new List<MedicationSuspension>();
        foreach (var (id, s) in _m.Suspensions)
        {
            if (s.CreatedHlc is not { } created || !Before(created) || Retracted(id)) continue;
            s.EndDate.TryGetAsOf(AsOf, out var end);
            list.Add(new MedicationSuspension
            {
                MedicineId = _m.Id,
                StartDate = s.StartDate,
                EndDate = end,
            });
        }
        return list;
    }

    public IEnumerable<StockEntryAdded> Entries()
        => _m.Entries.Values.Where(e => Before(e.Hlc) && !Retracted(e.FactId));

    public IEnumerable<StockCountRecorded> Counts()
        => _m.Counts.Values.Where(k => Before(k.Hlc) && !Retracted(k.FactId)).OrderBy(k => k.Hlc);

    public IEnumerable<(IntakeRecorded Intake, IntakeStatus Status)> Intakes()
    {
        foreach (var (id, i) in _m.Intakes)
        {
            if (i.Recorded is not { } rec || !Before(rec.Hlc) || Retracted(id)) continue;
            if (!i.Status.TryGetAsOf(AsOf, out var status)) continue;
            yield return (rec, status);
        }
    }

    public IEnumerable<LegacyMovement> LegacyMovements() => _m.LegacyMovements.Values;

    public IEnumerable<LegacyIntake> LegacyIntakes() => _m.LegacyIntakes.Values;

    public Medicine AsDomainMedicine() => new()
    {
        Id = _m.Id,
        Name = "prototype",
        Unit = _m.Created!.Unit,
        StartDate = StartDate,
        EndDate = EndDate,
    };

    private bool Before(Hlc hlc) => AsOf is not { } limit || hlc < limit;

    private bool Retracted(Guid factId) => _replica.IsRetracted(factId, AsOf);
}

// Pure derivation of the stock ledger from replicated facts
// (ANALYSIS-B1-MOBILE-SYNC.md §4.3, §4.4, §4.4b). Same inputs, same
// output, on every device.
public static class LedgerDeriver
{
    private static readonly StockMovementKind[] EpochAdvancingKinds =
    [
        StockMovementKind.NewPackage,
        StockMovementKind.ManualAdd,
        StockMovementKind.PositiveCorrection,
    ];

    public static DerivedLedger Derive(Replica replica, Guid medicineId, DateOnly today)
    {
        var m = replica.Medicines[medicineId];
        var current = new MedicineView(replica, m, asOf: null);
        if (!current.Exists) throw new InvalidOperationException("Medicine not created.");

        // Anchors in HLC order, each on its own snapshot.
        var anchors = new List<AnchorResult>();
        foreach (var count in current.Counts())
        {
            var view = new MedicineView(replica, m, count.Hlc);
            var visibleBefore = anchors.Where(a => !replica.IsRetracted(a.FactId, count.Hlc)).ToList();
            anchors.Add(EvaluateAnchor(view, count, visibleBefore));
        }

        var rows = Build(current, autoThrough: today.AddDays(-1), anchors);
        var raw = rows.Sum(r => r.Delta);

        var epoch = current.BaselineEpoch
            + current.Entries().Count(e => EpochAdvancingKinds.Contains(e.Kind))
            + anchors.Count(a => a.AdvancesEpoch);

        return new DerivedLedger(rows, raw, raw < 0m ? 0m : raw, epoch, current.CutoffDay, anchors);
    }

    // Values a ReconcileStock count dialog would show on this device now:
    // the quantity still scheduled for the count day.
    public static decimal TodayScheduled(Replica replica, Guid medicineId, DateOnly day, IReadOnlyList<AnchorResult> anchors)
    {
        var view = new MedicineView(replica, replica.Medicines[medicineId], asOf: null);
        return TodayScheduled(view, day, anchors);
    }

    private static AnchorResult EvaluateAnchor(MedicineView view, StockCountRecorded count, IReadOnlyList<AnchorResult> anchorsBefore)
    {
        var day = count.CountDay;
        var start = Build(view, autoThrough: day.AddDays(-1), anchorsBefore).Sum(r => r.Delta);
        var todayScheduled = TodayScheduled(view, day, anchorsBefore);

        // ReconcileStock / StockCountSnapshot.Evaluate, unchanged.
        var correction = count.Counted - (start - count.TakenToday);
        var materializes = todayScheduled > 0m && count.TakenToday == todayScheduled;
        var ledgerAfter = materializes ? count.Counted : count.Counted + count.TakenToday;
        var slots = view.SlotsOn(day);
        var schedule = view.ScheduleRows();
        var suspensions = view.Suspensions();
        var forecast = RunOutForecast.Compute(
            day,
            ledgerAfter,
            DailyConsumption.RateOn(day, schedule, slots),
            SuspensionState.IsSuspendedOn(day, suspensions));
        var advances = correction > 0m
            && !(forecast.DaysRemaining is int days && days <= count.ThresholdAtCount);

        return new AnchorResult(count.FactId, count.Hlc, day, correction, materializes, advances, todayScheduled);
    }

    private static decimal TodayScheduled(MedicineView view, DateOnly day, IReadOnlyList<AnchorResult> anchorsBefore)
    {
        if (!view.IsActiveOn(day)) return 0m;
        if (IntakeDays(view).Contains(day)) return 0m;
        if (anchorsBefore.Any(a => a.MaterializesCountDay && a.CountDay == day)) return 0m;
        return PlannedQuantity(view, day);
    }

    private static decimal PlannedQuantity(MedicineView view, DateOnly day)
    {
        var plan = ConsumptionMaterializer.Plan(
            view.AsDomainMedicine(), day, day, view.ScheduleRows(), view.Suspensions(), view.SlotsOn(day));
        return plan.Sum(p => p.Quantity);
    }

    private static HashSet<DateOnly> IntakeDays(MedicineView view)
    {
        var days = new HashSet<DateOnly>(view.LegacyIntakes().Select(i => i.Day));
        foreach (var (intake, _) in view.Intakes()) days.Add(intake.Day);
        return days;
    }

    private static List<DerivedRow> Build(MedicineView view, DateOnly autoThrough, IReadOnlyList<AnchorResult> anchors)
    {
        var cutoff = view.CutoffDay;
        var rows = new List<DerivedRow>();

        foreach (var l in view.LegacyMovements())
            rows.Add(new DerivedRow("legacy", l.Kind, l.Delta, DateOnly.FromDateTime(l.OccurredAt.UtcDateTime)));

        foreach (var e in view.Entries())
            rows.Add(new DerivedRow("entry", e.Kind, e.Delta, DateOnly.FromDateTime(e.OccurredAt.UtcDateTime)));

        // Rules 1 and 1b: intake consumption; reversal of a frozen day's
        // automatic consumption by the first intake recorded for it.
        var legacyIntakeDays = new HashSet<DateOnly>(view.LegacyIntakes().Select(i => i.Day));
        var firstIntakePerFrozenDay = view.Intakes()
            .Where(x => x.Intake.Day <= cutoff && !legacyIntakeDays.Contains(x.Intake.Day))
            .GroupBy(x => x.Intake.Day)
            .ToDictionary(g => g.Key, g => g.MinBy(x => x.Intake.Hlc).Intake.IntakeId);

        foreach (var (intake, status) in view.Intakes())
        {
            if (intake.Day <= cutoff
                && firstIntakePerFrozenDay.TryGetValue(intake.Day, out var first)
                && first == intake.IntakeId)
            {
                var legacyAuto = -view.LegacyMovements()
                    .Where(l => l.Kind == StockMovementKind.Consumption
                                && DateOnly.FromDateTime(l.OccurredAt.UtcDateTime) == intake.Day)
                    .Sum(l => l.Delta);
                if (legacyAuto > 0m)
                    rows.Add(new DerivedRow("reversal", StockMovementKind.PositiveCorrection, legacyAuto, intake.Day));
            }
            if (status == IntakeStatus.Taken)
                rows.Add(new DerivedRow("intake", StockMovementKind.Consumption, -intake.Quantity, intake.Day));
        }

        // Rule 2 (and the count-day materialization of rule 3).
        var intakeDays = IntakeDays(view);
        // A count that materialized its day fixed that day's quantity in
        // its own snapshot, as ReconcileStock writes it at count time; a
        // later intake for the day still removes it (RegisterIntake
        // reverses it today).
        var materializedByAnchor = new Dictionary<DateOnly, decimal>();
        foreach (var a in anchors)
            if (a.MaterializesCountDay && a.CountDay > cutoff && !materializedByAnchor.ContainsKey(a.CountDay))
                materializedByAnchor[a.CountDay] = a.TodayScheduled;

        foreach (var (d, q) in materializedByAnchor)
            if (!intakeDays.Contains(d))
                rows.Add(new DerivedRow("count-day", StockMovementKind.Consumption, -q, d));

        var first2 = cutoff.AddDays(1);
        if (view.StartDate > first2) first2 = view.StartDate;
        for (var d = first2; d <= autoThrough; d = d.AddDays(1))
        {
            if (materializedByAnchor.ContainsKey(d)) continue;
            if (!view.IsActiveOn(d) || intakeDays.Contains(d)) continue;
            var q = PlannedQuantity(view, d);
            if (q > 0m) rows.Add(new DerivedRow("auto", StockMovementKind.Consumption, -q, d));
        }

        // Rule 3: count corrections.
        foreach (var a in anchors)
        {
            if (a.Correction == 0m) continue;
            var kind = a.Correction > 0m ? StockMovementKind.PositiveCorrection : StockMovementKind.NegativeCorrection;
            rows.Add(new DerivedRow("count", kind, a.Correction, a.CountDay));
        }

        return rows;
    }
}
