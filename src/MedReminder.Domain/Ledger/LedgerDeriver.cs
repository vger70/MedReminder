using System.Globalization;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Domain.Ledger;

// Pure derivation of a medicine's stock ledger from its facts
// (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.3, §4.4, §4.4b). Same
// facts, same day, same time zone: same rows, on every device.
// Validated against today's use cases by the S9 parity harness
// (§18.9), ported to tests/MedReminder.Application.Tests/Ledger.
//
// Rules, each with its own day range (C = facts.CutoffDay):
//  1  Intake consumption: every non-legacy Taken intake books its
//     quantity on its day.
//  1b Reversal: the first non-legacy intake recorded for a day that
//     carries legacy consumption and no legacy intake reverses that
//     consumption, as RegisterIntake does today. Frozen rows are never
//     edited.
//  2  Automatic consumption, days in (C, today): a day with no intake,
//     no legacy consumption and no count-day materialization, on which
//     the medicine is active (D15), books the planned quantity
//     (ConsumptionMaterializer, unchanged).
//  3  Count anchors: each count books its stored correction and, when
//     it materialized its day, that day's quantity fixed at count time.
//     A later intake for the day removes the materialization.
//
// Epoch (§4.4): BaselineEpoch + positive user stock entries + counts
// that advanced it.
//
// Not applied yet (Phase 2c-2 wires the deriver into the use cases);
// Phase 2c-1 only proves parity with today's behavior.
public static class LedgerDeriver
{
    public static DerivedLedger Derive(LedgerFacts facts, DateOnly today, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(zone);

        var view = new FactView(facts, zone);
        var rows = Build(view, autoThrough: today.AddDays(-1), facts.Counts);
        var raw = rows.Sum(r => r.Delta);

        var epoch = facts.BaselineEpoch
            + facts.UserEntries.Count(IsEpochAdvancingEntry)
            + facts.Counts.Count(c => c.AdvancesEpoch);

        return new DerivedLedger(rows, raw, raw < 0m ? 0m : raw, epoch);
    }

    // Values of a stock count taken on countDay, evaluated on `facts`
    // (the facts recorded before the count, earlier counts included).
    // Same formula as ReconcileStock / StockCountSnapshot today.
    public static StockCountAnchor EvaluateCount(
        LedgerFacts facts,
        Guid countId,
        DateOnly countDay,
        DateTimeOffset recordedAt,
        decimal countedQuantity,
        decimal takenToday,
        int thresholdAtCount,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(zone);

        var view = new FactView(facts, zone);
        var start = Build(view, autoThrough: countDay.AddDays(-1), facts.Counts).Sum(r => r.Delta);
        var scheduled = CountDayScheduled(view, countDay, facts.Counts);

        if (countedQuantity < 0m)
            throw new ArgumentOutOfRangeException(nameof(countedQuantity), "Counted quantity cannot be negative.");
        if (takenToday < 0m || takenToday > scheduled)
            throw new ArgumentOutOfRangeException(nameof(takenToday),
                $"Quantity taken today must be between 0 and {scheduled}.");

        var correction = countedQuantity - (start - takenToday);
        var materializes = scheduled > 0m && takenToday == scheduled;
        var ledgerAfter = materializes ? countedQuantity : countedQuantity + takenToday;

        // The forecast uses the current slots, as the count dialog does.
        var forecast = RunOutForecast.Compute(
            countDay,
            ledgerAfter,
            DailyConsumption.RateOn(countDay, view.Schedule, view.CurrentSlots),
            SuspensionState.IsSuspendedOn(countDay, facts.Suspensions));
        var advances = correction > 0m
            && !(forecast.DaysRemaining is int days && days <= thresholdAtCount);

        return new StockCountAnchor(
            countId, countDay, recordedAt, countedQuantity, takenToday, thresholdAtCount,
            start, scheduled, correction, materializes, advances);
    }

    // Quantity still due on `day` that a count on that day can mark as
    // taken: zero when the day is inactive, has an intake, is already
    // booked by a frozen row or by an earlier count.
    public static decimal CountDayScheduled(LedgerFacts facts, DateOnly day, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(zone);
        return CountDayScheduled(new FactView(facts, zone), day, facts.Counts);
    }

    private static decimal CountDayScheduled(FactView view, DateOnly day, IReadOnlyList<StockCountAnchor> anchors)
    {
        if (!view.IsActiveOn(day)) return 0m;
        if (view.IntakeDays.Contains(day)) return 0m;
        if (view.LegacyConsumptionByDay.ContainsKey(day)) return 0m;
        if (anchors.Any(a => a.MaterializesCountDay && a.CountDay == day)) return 0m;
        return view.PlannedQuantity(day);
    }

    private static List<LedgerRow> Build(FactView view, DateOnly autoThrough, IReadOnlyList<StockCountAnchor> anchors)
    {
        var facts = view.Facts;
        var rows = new List<LedgerRow>();

        foreach (var m in facts.LegacyMovements)
            rows.Add(new LedgerRow(m.Id, DerivedRule.Legacy, m.Kind, m.QuantityDelta, view.LocalDay(m.OccurredAt), m.OccurredAt));
        foreach (var m in facts.UserEntries)
            rows.Add(new LedgerRow(m.Id, DerivedRule.UserEntry, m.Kind, m.QuantityDelta, view.LocalDay(m.OccurredAt), m.OccurredAt));

        // Rules 1 and 1b.
        var legacyIntakeDays = facts.Intakes.Where(i => i.IsLegacy).Select(i => i.Day).ToHashSet();
        var reversedDays = new HashSet<DateOnly>();
        foreach (var intake in facts.Intakes)
        {
            if (intake.IsLegacy) continue;

            if (!legacyIntakeDays.Contains(intake.Day)
                && reversedDays.Add(intake.Day)
                && view.LegacyConsumptionByDay.TryGetValue(intake.Day, out var legacyConsumed)
                && legacyConsumed > 0m)
            {
                rows.Add(view.Derived(DerivedRule.FrozenDayReversal, $"reversal:{Key(intake.Day)}",
                    StockMovementKind.PositiveCorrection, legacyConsumed, intake.Day));
            }

            if (intake.Status == IntakeStatus.Taken)
            {
                rows.Add(view.Derived(DerivedRule.IntakeConsumption, $"intake:{intake.Id:N}",
                    StockMovementKind.Consumption, -intake.Quantity, intake.Day));
            }
        }

        // Rule 3, count-day materialization: the first count that
        // materialized a day fixes its quantity.
        var materialized = new Dictionary<DateOnly, decimal>();
        foreach (var a in anchors)
        {
            if (a.MaterializesCountDay && a.CountDay > facts.CutoffDay)
                materialized.TryAdd(a.CountDay, a.CountDayScheduled);
        }
        foreach (var (day, quantity) in materialized)
        {
            if (view.IntakeDays.Contains(day)) continue;
            rows.Add(view.Derived(DerivedRule.CountDayConsumption, $"count-day:{Key(day)}",
                StockMovementKind.Consumption, -quantity, day));
        }

        // Rule 2.
        var first = facts.CutoffDay.AddDays(1);
        if (facts.StartDate > first) first = facts.StartDate;
        for (var day = first; day <= autoThrough; day = day.AddDays(1))
        {
            if (materialized.ContainsKey(day)) continue;
            if (view.IntakeDays.Contains(day)) continue;
            if (view.LegacyConsumptionByDay.ContainsKey(day)) continue;
            if (!view.IsActiveOn(day)) continue;

            var quantity = view.PlannedQuantity(day);
            if (quantity > 0m)
            {
                rows.Add(view.Derived(DerivedRule.AutomaticConsumption, $"auto:{Key(day)}",
                    StockMovementKind.Consumption, -quantity, day));
            }
        }

        // Rule 3, corrections.
        foreach (var a in anchors)
        {
            if (a.Correction == 0m) continue;
            var kind = a.Correction > 0m
                ? StockMovementKind.PositiveCorrection
                : StockMovementKind.NegativeCorrection;
            rows.Add(new LedgerRow(
                DeterministicGuid.Create(facts.MedicineId, $"count:{a.Id:N}"),
                DerivedRule.CountCorrection, kind, a.Correction, a.CountDay, a.RecordedAt));
        }

        return rows;
    }

    private static bool IsEpochAdvancingEntry(StockMovement entry)
        => entry.QuantityDelta > 0m
           && entry.Kind is StockMovementKind.NewPackage
               or StockMovementKind.ManualAdd
               or StockMovementKind.PositiveCorrection;

    private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Indexes over the facts shared by the rules.
    private sealed class FactView
    {
        private readonly TimeZoneInfo _zone;
        private readonly Medicine _medicine;

        public FactView(LedgerFacts facts, TimeZoneInfo zone)
        {
            Facts = facts;
            _zone = zone;
            _medicine = new Medicine
            {
                Id = facts.MedicineId,
                Name = string.Empty,
                Unit = string.Empty,
                StartDate = facts.StartDate,
                EndDate = facts.EndDate,
            };

            // §17: same EffectiveFrom, the later recorded row wins.
            Schedule = facts.Schedule
                .Select((row, index) => (row, index))
                .GroupBy(x => x.row.EffectiveFrom)
                .Select(g => g.MaxBy(x => x.index).row)
                .ToList();

            IntakeDays = facts.Intakes.Select(i => i.Day).ToHashSet();

            LegacyConsumptionByDay = facts.LegacyMovements
                .Where(m => m.Kind == StockMovementKind.Consumption)
                .GroupBy(m => LocalDay(m.OccurredAt))
                .ToDictionary(g => g.Key, g => -g.Sum(m => m.QuantityDelta));

            CurrentSlots = facts.SlotSets.Count == 0
                ? []
                : facts.SlotSets.MaxBy(s => s.RecordedAt)!.Slots;
        }

        public LedgerFacts Facts { get; }

        public IReadOnlyList<MedicationScheduleHistory> Schedule { get; }

        public HashSet<DateOnly> IntakeDays { get; }

        // Quantity booked by legacy Consumption rows, per local day.
        public Dictionary<DateOnly, decimal> LegacyConsumptionByDay { get; }

        // Slots of the latest recorded set, whatever its start.
        public IReadOnlyList<MedicationAdministrationSlot> CurrentSlots { get; }

        public bool IsActiveOn(DateOnly day)
        {
            LedgerActivity? latest = null;
            foreach (var change in Facts.Activity)
            {
                if (change.Day > day) continue;
                if (latest is null || change.RecordedAt >= latest.RecordedAt) latest = change;
            }
            return latest?.Active ?? true;
        }

        // Among the sets in force on `day`, the most recently recorded
        // one (§4.2).
        public IReadOnlyList<MedicationAdministrationSlot> SlotsOn(DateOnly day)
        {
            LedgerSlotSet? best = null;
            foreach (var set in Facts.SlotSets)
            {
                if (set.EffectiveFrom > day) continue;
                if (best is null || set.RecordedAt >= best.RecordedAt) best = set;
            }
            return best?.Slots ?? [];
        }

        public decimal PlannedQuantity(DateOnly day)
            => ConsumptionMaterializer
                .Plan(_medicine, day, day, Schedule, Facts.Suspensions, SlotsOn(day))
                .Sum(p => p.Quantity);

        public LedgerRow Derived(DerivedRule rule, string name, StockMovementKind kind, decimal delta, DateOnly day)
            => new(DeterministicGuid.Create(Facts.MedicineId, name), rule, kind, delta, day, LocalMidday(day));

        public DateOnly LocalDay(DateTimeOffset instant)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _zone).DateTime);

        private DateTimeOffset LocalMidday(DateOnly day)
        {
            var local = day.ToDateTime(new TimeOnly(12, 0));
            return new DateTimeOffset(local, _zone.GetUtcOffset(local));
        }
    }
}
