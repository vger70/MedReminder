using FluentAssertions;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Domain.Tests.Ledger;

// Unit tests of the rule mechanics. Parity with today's use cases is
// covered by tests/MedReminder.Application.Tests/Ledger.
public class LedgerDeriverTests
{
    private static readonly Guid MedicineId = Guid.Parse("0b0b0b0b-0000-0000-0000-000000000001");
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly Today = new(2026, 3, 11);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static LedgerFacts Facts(DateOnly? cutoff = null) => new()
    {
        MedicineId = MedicineId,
        StartDate = Start,
        CutoffDay = cutoff ?? Start.AddDays(-1),
        UserEntries = [Entry(StockMovementKind.InitialLoad, 50m, Start)],
        Schedule =
        [
            new MedicationScheduleHistory
            {
                MedicineId = MedicineId, EffectiveFrom = Start,
                DosePerAdministration = 1m, AdministrationsPerDay = 1,
            },
        ],
    };

    private static StockMovement Entry(StockMovementKind kind, decimal delta, DateOnly day) => new()
    {
        MedicineId = MedicineId,
        OccurredAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
        Kind = kind,
        QuantityDelta = delta,
        StockEpoch = 1,
    };

    private static DateTimeOffset At(DateOnly day, int hour) => new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    [Fact]
    public void Books_automatic_consumption_through_yesterday()
    {
        var ledger = LedgerDeriver.Derive(Facts(), Today, Utc);

        ledger.DerivedRows.Should().HaveCount(10)
            .And.OnlyContain(r => r.Rule == DerivedRule.AutomaticConsumption && r.Delta == -1m);
        ledger.DerivedRows.Max(r => r.Day).Should().Be(Today.AddDays(-1));
        ledger.Stock.Should().Be(40m);
        ledger.Epoch.Should().Be(1);
    }

    [Fact]
    public void Derived_ids_are_stable_across_runs_and_days()
    {
        var first = LedgerDeriver.Derive(Facts(), Today, Utc).DerivedRows.ToDictionary(r => r.Day, r => r.Id);
        var later = LedgerDeriver.Derive(Facts(), Today.AddDays(3), Utc).DerivedRows.ToDictionary(r => r.Day, r => r.Id);

        foreach (var (day, id) in first)
        {
            later[day].Should().Be(id);
        }
        first.Values.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Derived_ids_depend_on_the_medicine()
    {
        var a = LedgerDeriver.Derive(Facts(), Today, Utc).DerivedRows.First().Id;
        var b = LedgerDeriver.Derive(Facts() with { MedicineId = Guid.NewGuid() }, Today, Utc).DerivedRows.First().Id;

        a.Should().NotBe(b);
        a.Version.Should().Be(5);
    }

    [Fact]
    public void Days_up_to_the_cutoff_are_never_derived()
    {
        var cutoff = Start.AddDays(4);
        var ledger = LedgerDeriver.Derive(Facts(cutoff), Today, Utc);

        ledger.DerivedRows.Should().OnlyContain(r => r.Day > cutoff);
    }

    [Fact]
    public void Inactive_days_book_nothing()
    {
        var facts = Facts() with
        {
            Activity =
            [
                new LedgerActivity(Start.AddDays(2), false, At(Start.AddDays(2), 9)),
                new LedgerActivity(Start.AddDays(6), true, At(Start.AddDays(6), 9)),
            ],
        };

        var days = LedgerDeriver.Derive(facts, Today, Utc).DerivedRows.Select(r => r.Day).ToList();

        days.Should().NotContain(d => d >= Start.AddDays(2) && d < Start.AddDays(6));
        days.Should().HaveCount(6);
    }

    [Fact]
    public void Slot_sets_apply_from_their_effective_day_latest_recorded_wins()
    {
        MedicationAdministrationSlot Slot(decimal dose) => new() { MedicineId = MedicineId, Dose = dose };
        var facts = Facts() with
        {
            SlotSets =
            [
                new LedgerSlotSet(Start, At(Start, 8), [Slot(2m)]),
                new LedgerSlotSet(Start.AddDays(5), At(Start.AddDays(5), 8), [Slot(3m)]),
                // An empty set: back to dose x frequency from its start.
                new LedgerSlotSet(Start.AddDays(7), At(Start.AddDays(7), 8), []),
            ],
        };

        var byDay = LedgerDeriver.Derive(facts, Today, Utc).DerivedRows.ToDictionary(r => r.Day, r => -r.Delta);

        byDay[Start.AddDays(4)].Should().Be(2m);
        byDay[Start.AddDays(5)].Should().Be(3m);
        byDay[Start.AddDays(7)].Should().Be(1m, "an empty set falls back to dose x frequency");
    }

    [Fact]
    public void An_intake_day_gets_no_automatic_consumption()
    {
        var day = Start.AddDays(3);
        var facts = Facts() with
        {
            Intakes = [new LedgerIntake(Guid.NewGuid(), day, IntakeStatus.Taken, 2m, At(day, 20), IsLegacy: false)],
        };

        var rows = LedgerDeriver.Derive(facts, Today, Utc).DerivedRows.Where(r => r.Day == day).ToList();

        rows.Should().ContainSingle().Which.Should().Match<LedgerRow>(r =>
            r.Rule == DerivedRule.IntakeConsumption && r.Delta == -2m);
    }

    [Fact]
    public void DailyConsumption_takes_the_later_of_two_rows_with_the_same_date()
    {
        // §17: callers pass rows in recording order within a date.
        MedicationScheduleHistory Row(int admins) => new()
        {
            MedicineId = MedicineId, EffectiveFrom = Start,
            DosePerAdministration = 1m, AdministrationsPerDay = admins,
        };

        MedReminder.Domain.Calculations.DailyConsumption.RateOn(Start.AddDays(1), [Row(2), Row(3)])
            .Should().Be(3m);
    }

    [Fact]
    public void Positive_user_entries_and_advancing_counts_raise_the_epoch()
    {
        var facts = Facts() with
        {
            BaselineEpoch = 3,
            UserEntries =
            [
                Entry(StockMovementKind.InitialLoad, 50m, Start),
                Entry(StockMovementKind.NewPackage, 28m, Start.AddDays(2)),
                Entry(StockMovementKind.NegativeCorrection, -2m, Start.AddDays(3)),
            ],
            Counts =
            [
                new StockCountAnchor(Guid.NewGuid(), Start.AddDays(4), At(Start.AddDays(4), 9),
                    60m, 0m, 5, 70m, 1m, -10m, false, false),
                new StockCountAnchor(Guid.NewGuid(), Start.AddDays(5), At(Start.AddDays(5), 9),
                    90m, 0m, 5, 59m, 1m, 31m, false, true),
            ],
        };

        LedgerDeriver.Derive(facts, Today, Utc).Epoch.Should().Be(5);
    }

    [Fact]
    public void Each_epoch_is_identified_by_the_fact_that_opened_it()
    {
        var refill = Entry(StockMovementKind.NewPackage, 28m, Start.AddDays(2));
        var facts = Facts() with
        {
            BaselineEpoch = 2,
            UserEntries = [Entry(StockMovementKind.InitialLoad, 50m, Start), refill],
        };

        var ledger = LedgerDeriver.Derive(facts, Today, Utc);

        ledger.Epoch.Should().Be(3);
        ledger.EpochFactId.Should().Be(refill.Id);
        ledger.EpochFactIds.Keys.Should().Equal(2, 3);
        ledger.EpochFactIds[2].Should().Be(LedgerDeriver.Derive(Facts() with { BaselineEpoch = 2 }, Today, Utc).EpochFactId);
    }

    [Fact]
    public void NotificationCycle_compares_epoch_fact_ids_when_both_are_known()
    {
        var medicine = new Medicine
        {
            Id = MedicineId, Name = "m", Unit = "u", StartDate = Start, ThresholdDays = 7,
            StockEpoch = 2, StockEpochFactId = Guid.NewGuid(),
            NotificationChannels = MedReminder.Domain.Notifications.NotificationChannels.Windows,
        };
        MedReminder.Domain.Notifications.NotificationEvent Event(Guid? factId) => new()
        {
            MedicineId = MedicineId, StockEpoch = 2, EpochFactId = factId, TriggeredAt = At(Today, 9),
            Channel = MedReminder.Domain.Notifications.NotificationChannels.Windows,
            DaysRemainingAtSend = 3, Success = true,
        };

        MedReminder.Domain.Calculations.NotificationCycle.ShouldNotify(medicine, 3, null, Event(Guid.NewGuid()))
            .Should().BeTrue("same number, another epoch");
        MedReminder.Domain.Calculations.NotificationCycle.ShouldNotify(medicine, 3, null, Event(medicine.StockEpochFactId))
            .Should().BeFalse();
        MedReminder.Domain.Calculations.NotificationCycle.ShouldNotify(medicine, 3, null, Event(null))
            .Should().BeFalse("older events compare the number");
    }

    [Fact]
    public void EvaluateCount_applies_the_ReconcileStock_formula()
    {
        // Start of Today: 50 - 10 days. Counted 38, 1 of today's 1 taken:
        // expected 39, correction -1, today materialized.
        var anchor = LedgerDeriver.EvaluateCount(
            Facts(), Guid.NewGuid(), Today, At(Today, 20), 38m, 1m, 5, Utc);

        anchor.LedgerAtStartOfDay.Should().Be(40m);
        anchor.CountDayScheduled.Should().Be(1m);
        anchor.Correction.Should().Be(-1m);
        anchor.MaterializesCountDay.Should().BeTrue();
        anchor.AdvancesEpoch.Should().BeFalse();

        var after = LedgerDeriver.Derive(Facts() with { Counts = [anchor] }, Today, Utc);
        after.Stock.Should().Be(38m);
    }

    [Fact]
    public void EvaluateCount_rejects_more_taken_than_scheduled()
    {
        FluentActions.Invoking(() => LedgerDeriver.EvaluateCount(
                Facts(), Guid.NewGuid(), Today, At(Today, 20), 38m, 2m, 5, Utc))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
