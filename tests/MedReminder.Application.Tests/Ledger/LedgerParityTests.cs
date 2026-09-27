using FluentAssertions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Ledger;

// The ledger the application stores equals a derivation from the facts
// recorded independently by the harness, after every action of random
// scenarios (B.1 Phase 2c-2). Retroactive changes (D6, D15, same-date
// schedule rows) and fact retractions (Phase 2d) are included: since
// 2c-2 the use cases derive too.
// Phase 2c-1 ran these scenarios without them against the former use
// cases and found parity on 10 000 seeds (ANALYSIS-B1-MOBILE-SYNC.md §13).
//
// LEDGER_PARITY_SEEDS raises the number of random scenarios (default
// 300); LEDGER_PARITY_FIRST_SEED replays one seed.
public sealed class LedgerParityTests
{
    private static int FirstSeed
        => int.TryParse(Environment.GetEnvironmentVariable("LEDGER_PARITY_FIRST_SEED"), out var f) ? f : 1;

    private static int Seeds
        => int.TryParse(Environment.GetEnvironmentVariable("LEDGER_PARITY_SEEDS"), out var n) ? n : 300;

    [Fact]
    public void Derived_ledger_matches_use_cases_on_random_scenarios()
    {
        string? failure = null;
        for (var seed = FirstSeed; seed < FirstSeed + Seeds && failure is null; seed++)
        {
            try
            {
                RunScenario(seed, patchOnDay: seed % 3 == 0 ? 12 : null);
            }
            catch (LedgerParityException ex)
            {
                failure = $"seed {seed}: {ex.Message}";
            }
        }
        failure.Should().BeNull();
    }

    [Fact]
    public void Backdated_intake_on_frozen_day_reverses_legacy_consumption()
    {
        var h = new LedgerParityHarness();
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
        var h = new LedgerParityHarness();
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

    // The boot patch can run late in a day on which a count already
    // materialized today's consumption: that frozen row must keep the
    // day booked after the cutoff (rule 2) and be reversed by the first
    // later intake (rule 1b).
    [Fact]
    public void Patch_after_a_count_that_materialized_today()
    {
        var h = new LedgerParityHarness();
        h.StartDay(0);
        h.AddMedicine(h.Today, 1m, 2, 30m, 5, null, null);
        var id = h.Medicines[0];
        h.StartDay(1);
        h.AdvanceTo(TimeSpan.FromHours(9));
        h.Count(id, 26m, scheduled => scheduled);
        h.AdvanceTo(TimeSpan.FromHours(10));
        h.ApplyCutoffPatch();
        h.Compare();
        h.AdvanceTo(TimeSpan.FromHours(11));
        h.Intake(id, h.Today, IntakeStatus.Taken, 1m);
        h.Compare();
        h.StartDay(2);
        h.Compare();
    }

    [Fact]
    public void Patch_after_a_count_that_materialized_today_keeps_the_day_booked()
    {
        var h = new LedgerParityHarness();
        h.StartDay(0);
        h.AddMedicine(h.Today, 1m, 2, 30m, 5, null, null);
        var id = h.Medicines[0];
        h.StartDay(1);
        h.AdvanceTo(TimeSpan.FromHours(9));
        h.Count(id, 26m, scheduled => scheduled);
        h.AdvanceTo(TimeSpan.FromHours(10));
        h.ApplyCutoffPatch();
        h.StartDay(2);
        h.Compare();
    }

    [Fact]
    public void Retractions_keep_the_stored_ledger_equal_to_the_derivation()
    {
        var h = new LedgerParityHarness();
        h.StartDay(0);
        h.AddMedicine(h.Today, 1m, 2, 30m, 5, null, null);
        var id = h.Medicines[0];
        h.StartDay(3);
        h.AdvanceTo(TimeSpan.FromHours(9));
        h.AddStock(id, 28m, StockMovementKind.NewPackage);
        h.AdvanceTo(TimeSpan.FromHours(10));
        h.Intake(id, h.Today.AddDays(-1), IntakeStatus.Taken, 1m);
        h.AdvanceTo(TimeSpan.FromHours(11));
        h.Count(id, 50m, _ => 0m);
        h.Compare();

        var rng = new Random(7);
        for (var i = 0; i < 3; i++)
        {
            h.AdvanceTo(TimeSpan.FromHours(12 + i));
            h.RetractRandom(id, rng);
            h.Compare();
        }
        h.Trace.Should().Contain(t => t.Contains("retract"));
        h.StartDay(4);
        h.Compare();
    }

    [Fact]
    public void Deactivation_stops_automatic_consumption()
    {
        var h = new LedgerParityHarness();
        h.StartDay(0);
        h.AddMedicine(h.Today, 1m, 1, 30m, 5, null, null);
        var id = h.Medicines[0];
        h.StartDay(3);
        h.AdvanceTo(TimeSpan.FromHours(9));
        h.Deactivate(id);
        for (var day = 4; day <= 8; day++) { h.StartDay(day); h.Compare(); }
    }

    private static void RunScenario(int seed, int? patchOnDay)
    {
        var rng = new Random(seed);
        var h = new LedgerParityHarness();
        const int days = 25;
        for (var day = 0; day < days; day++)
        {
            h.StartDay(day);
            var minutes = Enumerable.Range(0, rng.Next(0, 5))
                .Select(_ => rng.Next(6 * 60, 23 * 60)).Order().ToList();
            // The patch runs at app start, possibly after some actions
            // of the same day.
            var patchAfter = patchOnDay == day ? rng.Next(0, minutes.Count + 1) : -1;
            if (patchAfter == 0) h.ApplyCutoffPatch();
            h.Compare();

            for (var i = 0; i < minutes.Count; i++)
            {
                h.AdvanceTo(TimeSpan.FromMinutes(minutes[i]));
                RandomAction(h, rng);
                h.Compare();
                if (patchAfter == i + 1)
                {
                    h.ApplyCutoffPatch();
                    h.Compare();
                }
            }
        }
    }

    private static void RandomAction(LedgerParityHarness h, Random rng)
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
        switch (rng.Next(0, 13))
        {
            case 12:
                h.RetractRandom(id, rng);
                break;
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
                h.ChangeSchedule(id, h.Today.AddDays(rng.Next(-5, 6)), rng.Next(1, 4) * 0.5m, rng.Next(1, 4));
                break;
            case 7:
                h.Suspend(id, h.Today.AddDays(rng.Next(0, 4)));
                break;
            case 8:
                var open = h.App.Suspensions.GetOpenSuspensionAsync(id, default).GetAwaiter().GetResult();
                if (open is null) break;
                var min = open.StartDate > h.Today ? open.StartDate : h.Today;
                h.Resume(id, min.AddDays(rng.Next(0, 4)));
                break;
            case 9:
                h.Update(id, threshold: rng.Next(0, 15));
                break;
            case 10:
                h.Update(id, setEnd: true, end: rng.NextDouble() < 0.3 ? null : h.Today.AddDays(rng.Next(-5, 20)));
                break;
            case 11:
                var roll = rng.NextDouble();
                if (roll < 0.5)
                    h.Update(id, slots: rng.NextDouble() < 0.2 ? [] : RandomSlots(rng));
                else if (roll < 0.65)
                    h.Deactivate(id);
                else if (roll < 0.8)
                    h.Update(id, active: true);
                break;
        }
    }

    private static List<AdministrationSlotInput> RandomSlots(Random rng)
        => Enumerable.Range(0, rng.Next(1, 4))
            .Select(i => new AdministrationSlotInput(rng.Next(1, 3) * 0.5m, new TimeOnly(8 + i * 5, 0), null))
            .ToList();
}
