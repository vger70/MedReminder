using FluentAssertions;
using MedReminder.Application.Coverage;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Application.Tests.Coverage;

// Coverage planner (docs/notes/EVOLUTION-PROPOSALS-2.md §3.5): the pure
// rule over one or more medicines and a period.
public class CoveragePlannerTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static Medicine Medicine(string name = "Enalapril", DateOnly? start = null, DateOnly? end = null,
        bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Unit = "tablets",
        StartDate = start ?? new DateOnly(2026, 1, 1),
        EndDate = end,
        IsActive = active,
        DosePerAdministration = 1m,
        AdministrationsPerDay = 2,
    };

    private static MedicationScheduleHistory Daily(Medicine m, decimal dose, int times, DateOnly? from = null) => new()
    {
        MedicineId = m.Id,
        EffectiveFrom = from ?? m.StartDate,
        DosePerAdministration = dose,
        AdministrationsPerDay = times,
    };

    private static CoverageInput Input(Medicine m, decimal stock, decimal? package = null,
        IReadOnlyList<MedicationScheduleHistory>? schedule = null,
        IReadOnlyList<MedicationSuspension>? suspensions = null,
        IReadOnlyList<MedicationAdministrationSlot>? slots = null)
        => new(m, schedule ?? [Daily(m, 1m, 2)], suspensions ?? [], slots ?? [], stock, package);

    [Fact]
    public void A_period_starting_today_needs_every_day_of_it()
    {
        var m = Medicine();
        // 2 a day for 14 days = 28; stock 20.
        var row = CoveragePlanner.BuildRow(Input(m, 20m, package: 28m), Today, Today, Today.AddDays(13));

        row.Needed.Should().Be(28m);
        row.StockAtStart.Should().Be(20m);
        row.Shortfall.Should().Be(8m);
        row.Packages.Should().Be(1);
        row.Status.Should().Be(CoverageStatus.Short);
    }

    [Fact]
    public void The_use_before_the_period_is_taken_from_the_stock_including_today()
    {
        var m = Medicine();
        // Period starts in 5 days: today to day 4 use 10 of 30, the
        // 7-day period needs 14.
        var row = CoveragePlanner.BuildRow(Input(m, 30m), Today, Today.AddDays(5), Today.AddDays(11));

        row.StockAtStart.Should().Be(20m);
        row.Needed.Should().Be(14m);
        row.Shortfall.Should().Be(0m);
        row.Packages.Should().BeNull();
        row.Status.Should().Be(CoverageStatus.Covered);
    }

    [Fact]
    public void A_stock_that_runs_out_before_the_period_misses_the_whole_period()
    {
        var m = Medicine();
        var row = CoveragePlanner.BuildRow(Input(m, 4m, package: 10m), Today, Today.AddDays(5), Today.AddDays(9));

        row.StockAtStart.Should().Be(-6m);
        row.Needed.Should().Be(10m);
        row.Shortfall.Should().Be(10m);
        row.Packages.Should().Be(1);
    }

    [Fact]
    public void Packages_round_up_and_are_unknown_without_a_recorded_package()
    {
        var m = Medicine();
        CoveragePlanner.BuildRow(Input(m, 0m, package: 28m), Today, Today, Today.AddDays(29))
            .Packages.Should().Be(3, "60 tablets in packages of 28");
        CoveragePlanner.BuildRow(Input(m, 0m), Today, Today, Today.AddDays(29))
            .Packages.Should().BeNull();
    }

    [Fact]
    public void Suspended_days_and_days_after_the_therapy_end_need_nothing()
    {
        var m = Medicine(end: Today.AddDays(9));
        var suspension = new MedicationSuspension
        {
            MedicineId = m.Id, StartDate = Today.AddDays(2), EndDate = Today.AddDays(4),
        };
        // Days 0-9 in the therapy, 3 of them suspended: 7 days × 2.
        var row = CoveragePlanner.BuildRow(Input(m, 100m, suspensions: [suspension]),
            Today, Today, Today.AddDays(29));

        row.Needed.Should().Be(14m);
    }

    [Fact]
    public void A_schedule_change_inside_the_period_is_followed()
    {
        var m = Medicine();
        var schedule = new[] { Daily(m, 1m, 2), Daily(m, 1m, 1, Today.AddDays(5)) };
        // 5 days × 2 + 5 days × 1.
        var row = CoveragePlanner.BuildRow(Input(m, 0m, schedule: schedule), Today, Today, Today.AddDays(9));

        row.Needed.Should().Be(15m);
    }

    [Fact]
    public void A_cyclic_schedule_counts_only_its_on_days()
    {
        var m = Medicine();
        var (kind, payload) = ScheduleCodec.Serialize(new CyclicSchedule(21, 7, 1m));
        var schedule = new[]
        {
            new MedicationScheduleHistory
            {
                MedicineId = m.Id, EffectiveFrom = Today, DosePerAdministration = 1m, AdministrationsPerDay = 1,
                ScheduleKind = kind, SchedulePayload = payload,
            },
        };
        var row = CoveragePlanner.BuildRow(Input(m, 0m, schedule: schedule), Today, Today, Today.AddDays(27));

        row.Needed.Should().Be(21m);
    }

    [Fact]
    public void Slots_take_precedence_over_the_schedule()
    {
        var m = Medicine();
        var slots = new[]
        {
            new MedicationAdministrationSlot { MedicineId = m.Id, Dose = 0.5m, Order = 0 },
            new MedicationAdministrationSlot { MedicineId = m.Id, Dose = 1m, Order = 1 },
        };
        var row = CoveragePlanner.BuildRow(Input(m, 0m, slots: slots), Today, Today, Today.AddDays(3));

        row.Needed.Should().Be(6m);
    }

    [Fact]
    public void An_as_needed_medicine_is_not_computed()
    {
        var m = Medicine();
        var schedule = new[]
        {
            new MedicationScheduleHistory
            {
                MedicineId = m.Id, EffectiveFrom = m.StartDate, DosePerAdministration = 1m,
                AdministrationsPerDay = 1, ScheduleKind = ScheduleKind.Prn,
            },
        };
        var row = CoveragePlanner.BuildRow(Input(m, 3m, schedule: schedule), Today, Today, Today.AddDays(6));

        row.Needed.Should().Be(0m);
        row.Status.Should().Be(CoverageStatus.AsNeeded);
    }

    [Fact]
    public void A_medicine_whose_slots_are_all_as_needed_is_not_computed()
    {
        var m = Medicine();
        var slots = new[]
        {
            new MedicationAdministrationSlot { MedicineId = m.Id, Dose = 1m, Order = 0, IsAsNeeded = true },
        };
        var row = CoveragePlanner.BuildRow(Input(m, 3m, slots: slots), Today, Today, Today.AddDays(6));

        row.Needed.Should().Be(0m);
        row.Status.Should().Be(CoverageStatus.AsNeeded);
    }

    [Fact]
    public void An_as_needed_slot_beside_scheduled_ones_is_left_out_of_the_need()
    {
        var m = Medicine();
        var slots = new[]
        {
            new MedicationAdministrationSlot { MedicineId = m.Id, Dose = 1m, Order = 0 },
            new MedicationAdministrationSlot { MedicineId = m.Id, Dose = 1m, Order = 1, IsAsNeeded = true },
        };
        var row = CoveragePlanner.BuildRow(Input(m, 0m, slots: slots), Today, Today, Today.AddDays(3));

        row.Needed.Should().Be(4m);
        row.Status.Should().Be(CoverageStatus.Short);
    }

    [Fact]
    public void A_medicine_not_taken_in_the_period_is_marked_as_such()
    {
        var m = Medicine(start: Today.AddDays(30));
        var row = CoveragePlanner.BuildRow(Input(m, 0m), Today, Today, Today.AddDays(6));

        row.Status.Should().Be(CoverageStatus.NotInUse);
    }

    [Fact]
    public void The_plan_lists_active_medicines_with_the_missing_ones_first()
    {
        var covered = Medicine("Aspirin");
        var shortA = Medicine("Zocor");
        var shortB = Medicine("Bisoprolol");
        var inactive = Medicine("Old", active: false);

        var plan = CoveragePlanner.Build(
            [Input(covered, 100m), Input(shortA, 0m), Input(shortB, 1m), Input(inactive, 0m)],
            Today, Today, Today.AddDays(6));

        plan.Rows.Select(r => r.Name).Should().Equal("Bisoprolol", "Zocor", "Aspirin");
        plan.ShortCount.Should().Be(2);
        plan.Days.Should().Be(7);
    }

    [Fact]
    public void The_period_must_start_today_or_later_end_after_it_starts_and_stay_within_a_year()
    {
        var input = new[] { Input(Medicine(), 10m) };

        FluentActions.Invoking(() => CoveragePlanner.Build(input, Today, Today.AddDays(-1), Today))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => CoveragePlanner.Build(input, Today, Today.AddDays(2), Today.AddDays(1)))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => CoveragePlanner.Build(input, Today, Today, Today.AddDays(CoveragePlanner.MaxDays)))
            .Should().Throw<ArgumentOutOfRangeException>();
        CoveragePlanner.Build(input, Today, Today, Today.AddDays(CoveragePlanner.MaxDays - 1)).Days
            .Should().Be(CoveragePlanner.MaxDays);
    }
}
