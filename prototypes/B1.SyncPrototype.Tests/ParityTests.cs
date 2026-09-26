using FluentAssertions;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Prototypes.Sync.Tests;

// S9 acceptance (a): for every user action that is not retroactive, the
// derived ledger reproduces today's stock and StockEpoch exactly.
// Retroactive changes are excluded here on purpose: D6 and D15 change
// their behavior (see DocumentedDifferenceTests).
public sealed class ParityTests
{
    public static int FirstSeed => int.TryParse(Environment.GetEnvironmentVariable("S9_FIRST_SEED"), out var f) ? f : 1;

    public static int Seeds => int.TryParse(Environment.GetEnvironmentVariable("S9_PARITY_SEEDS"), out var n) ? n : 300;

    [Fact]
    public void Derived_ledger_matches_use_cases_on_random_scenarios()
    {
        var failures = new List<string>();
        for (var seed = FirstSeed; seed < FirstSeed + Seeds; seed++)
        {
            try
            {
                RunScenario(seed, patchOnDay: seed % 3 == 0 ? 12 : null);
            }
            catch (ParityException ex)
            {
                failures.Add($"seed {seed}: {ex.Message}");
                if (failures.Count >= 1) break;
            }
        }
        failures.Should().BeEmpty();
    }

    [Fact]
    public void Backdated_intake_on_frozen_day_reverses_legacy_consumption()
    {
        var h = new ParityHarness();
        h.StartDay(0);
        h.AddMedicine(h.Today, 1m, 2, 30m, 5, null, null);
        var id = h.Medicines[0];
        for (var day = 1; day <= 5; day++) { h.StartDay(day); h.Compare(); }
        h.ApplyCutoffPatch();
        h.Compare();
        h.AdvanceTo(TimeSpan.FromHours(10));
        h.Intake(id, h.Today.AddDays(-3), IntakeStatus.Taken, 1m);
        h.Intake(id, h.Today.AddDays(-3), IntakeStatus.Taken, 1m);
        h.Intake(id, h.Today.AddDays(-2), IntakeStatus.Skipped, 2m);
        h.Compare();
    }

    [Fact]
    public void Count_that_takes_all_of_today_then_intake_on_the_same_day()
    {
        var h = new ParityHarness();
        h.StartDay(0);
        h.AddMedicine(h.Today, 1m, 2, 30m, 5, null, null);
        var id = h.Medicines[0];
        h.StartDay(1);
        h.AdvanceTo(TimeSpan.FromHours(20));
        h.Count(id, 25m, scheduled => scheduled);
        h.Compare();
        h.AdvanceTo(TimeSpan.FromHours(21));
        h.Intake(id, h.Today, IntakeStatus.Taken, 1m);
        h.Compare();
        h.StartDay(2);
        h.Compare();
    }

    private static void RunScenario(int seed, int? patchOnDay)
    {
        var rng = new Random(seed);
        var h = new ParityHarness();
        const int days = 25;
        for (var day = 0; day < days; day++)
        {
            h.StartDay(day);
            if (patchOnDay == day) h.ApplyCutoffPatch();
            h.Compare();

            var actions = rng.Next(0, 5);
            var times = Enumerable.Range(0, actions).Select(_ => rng.Next(6 * 60, 23 * 60)).Order().ToList();
            foreach (var minute in times)
            {
                h.AdvanceTo(TimeSpan.FromMinutes(minute));
                RandomAction(h, rng);
                h.Compare();
            }
        }
    }

    private static void RandomAction(ParityHarness h, Random rng)
    {
        if (h.Medicines.Count == 0 || rng.NextDouble() < 0.08)
        {
            var start = h.Today.AddDays(rng.Next(-10, 3));
            var slots = rng.NextDouble() < 0.3 ? RandomSlots(rng) : null;
            DateOnly? end = rng.NextDouble() < 0.2 ? start.AddDays(rng.Next(5, 40)) : null;
            h.AddMedicine(start, rng.Next(1, 4) * 0.5m, rng.Next(1, 4), rng.Next(0, 61), rng.Next(3, 11), end, slots);
            return;
        }

        var id = h.Medicines[rng.Next(h.Medicines.Count)];
        switch (rng.Next(0, 12))
        {
            case 0:
                var kinds = new[] { StockMovementKind.NewPackage, StockMovementKind.ManualAdd, StockMovementKind.PositiveCorrection };
                h.AddStock(id, rng.Next(1, 31), kinds[rng.Next(kinds.Length)]);
                break;
            case 1:
                h.AdjustDown(id, rng.Next(1, 11));
                break;
            case 2:
            case 3:
                var statuses = Enum.GetValues<IntakeStatus>();
                h.Intake(id, h.Today.AddDays(-rng.Next(0, 5)), statuses[rng.Next(statuses.Length)], rng.Next(1, 4));
                break;
            case 4:
            case 5:
                h.Count(id, rng.Next(0, 61), scheduled => rng.Next(3) switch
                {
                    0 => 0m,
                    1 => scheduled,
                    _ => scheduled / 2m,
                });
                break;
            case 6:
                var from = h.Today.AddDays(rng.Next(0, 6));
                var existing = h.Oracle.Schedules.ListForMedicineAsync(id, default).GetAwaiter().GetResult();
                if (existing.Any(r => r.EffectiveFrom == from)) break;
                h.ChangeSchedule(id, from, rng.Next(1, 4) * 0.5m, rng.Next(1, 4));
                break;
            case 7:
                h.Suspend(id, h.Today.AddDays(rng.Next(0, 4)));
                break;
            case 8:
                var open = h.Oracle.Suspensions.GetOpenSuspensionAsync(id, default).GetAwaiter().GetResult();
                if (open is null) break;
                var min = open.StartDate > h.Today ? open.StartDate : h.Today;
                h.Resume(id, min.AddDays(rng.Next(0, 4)));
                break;
            case 9:
                h.Update(id, threshold: rng.Next(0, 15));
                break;
            case 10:
                // Non-retroactive only: an end date already in the past
                // re-opens closed days when moved (D6).
                var current = h.Oracle.Medicines.GetAsync(id, default).GetAwaiter().GetResult()!.EndDate;
                if (current is { } c && c < h.Today.AddDays(-1)) break;
                h.Update(id, setEnd: true, end: rng.NextDouble() < 0.3 ? null : h.Today.AddDays(rng.Next(0, 20)));
                break;
            case 11:
                if (rng.NextDouble() < 0.5)
                    h.Update(id, slots: rng.NextDouble() < 0.2 ? [] : RandomSlots(rng));
                else if (rng.NextDouble() < 0.2)
                    h.Update(id, active: false);
                break;
        }
    }

    private static List<SlotValue> RandomSlots(Random rng)
        => Enumerable.Range(0, rng.Next(1, 4))
            .Select(i => new SlotValue(rng.Next(1, 3) * 0.5m, new TimeOnly(8 + i * 5, 0)))
            .ToList();
}
