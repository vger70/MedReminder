using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Tests.Support;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

public class DailyConsumptionTests
{
    [Fact]
    public void RateOn_returns_zero_with_empty_history()
    {
        DailyConsumption
            .RateOn(new DateOnly(2026, 3, 1), Array.Empty<MedicationScheduleHistory>())
            .Should().Be(0m);
    }

    [Fact]
    public void RateOn_returns_dose_times_frequency_for_single_active_entry()
    {
        var schedule = new[]
        {
            DomainFactory.Schedule(new DateOnly(2026, 1, 1), dosePerAdministration: 1m, administrationsPerDay: 2),
        };

        DailyConsumption.RateOn(new DateOnly(2026, 3, 15), schedule)
            .Should().Be(2m);
    }

    [Fact]
    public void RateOn_uses_latest_entry_when_multiple_entries_are_active()
    {
        var schedule = new[]
        {
            DomainFactory.Schedule(new DateOnly(2026, 1, 1), 1m, 2),   // 2/gg
            DomainFactory.Schedule(new DateOnly(2026, 3, 1), 1m, 3),   // 3/gg dal 1° marzo
        };

        DailyConsumption.RateOn(new DateOnly(2026, 5, 10), schedule)
            .Should().Be(3m);
    }

    [Fact]
    public void RateOn_uses_earlier_entry_when_target_precedes_later_entry()
    {
        var schedule = new[]
        {
            DomainFactory.Schedule(new DateOnly(2026, 1, 1), 1m, 2),
            DomainFactory.Schedule(new DateOnly(2026, 3, 1), 1m, 3),
        };

        DailyConsumption.RateOn(new DateOnly(2026, 2, 20), schedule)
            .Should().Be(2m);
    }

    [Fact]
    public void RateOn_returns_zero_when_date_precedes_all_entries()
    {
        var schedule = new[]
        {
            DomainFactory.Schedule(new DateOnly(2026, 1, 1), 1m, 2),
        };

        DailyConsumption.RateOn(new DateOnly(2025, 12, 31), schedule)
            .Should().Be(0m);
    }

    [Fact]
    public void RateOn_includes_entry_effective_on_exact_target_date()
    {
        var schedule = new[]
        {
            DomainFactory.Schedule(new DateOnly(2026, 3, 15), 1m, 4),
        };

        DailyConsumption.RateOn(new DateOnly(2026, 3, 15), schedule)
            .Should().Be(4m);
    }

    // ---------- A1 schedule-kind dispatch --------------------------

    [Fact]
    public void RateOn_dispatches_through_weekly_schedule()
    {
        var effectiveFrom = new DateOnly(2026, 1, 5); // Monday
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(effectiveFrom,
                new WeeklySchedule(new[] { 1m, 2m, 3m, 4m, 5m, 6m, 7m })),
        };

        // Wednesday of the same week → index 2 → 3m
        DailyConsumption.RateOn(new DateOnly(2026, 1, 7), schedule).Should().Be(3m);
        // Sunday → index 6 → 7m
        DailyConsumption.RateOn(new DateOnly(2026, 1, 11), schedule).Should().Be(7m);
    }

    [Fact]
    public void RateOn_dispatches_through_cyclic_schedule()
    {
        var effectiveFrom = new DateOnly(2026, 3, 1);
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(effectiveFrom, new CyclicSchedule(3, 2, 1m)),
        };

        DailyConsumption.RateOn(effectiveFrom,                 schedule).Should().Be(1m);
        DailyConsumption.RateOn(effectiveFrom.AddDays(2),      schedule).Should().Be(1m);
        DailyConsumption.RateOn(effectiveFrom.AddDays(3),      schedule).Should().Be(0m);
        DailyConsumption.RateOn(effectiveFrom.AddDays(4),      schedule).Should().Be(0m);
        DailyConsumption.RateOn(effectiveFrom.AddDays(5),      schedule).Should().Be(1m);
    }

    [Fact]
    public void RateOn_dispatches_through_tapering_schedule()
    {
        var effectiveFrom = new DateOnly(2026, 3, 1);
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(effectiveFrom,
                new TaperingSchedule(startDose: 4m, endDose: 0.5m, step: 0.5m, intervalDays: 7)),
        };

        DailyConsumption.RateOn(effectiveFrom,                schedule).Should().Be(4m);
        DailyConsumption.RateOn(effectiveFrom.AddDays(7),     schedule).Should().Be(3.5m);
        DailyConsumption.RateOn(effectiveFrom.AddDays(7 * 7), schedule).Should().Be(0.5m);
    }

    [Fact]
    public void RateOn_returns_zero_for_prn_schedule()
    {
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(new DateOnly(2026, 3, 1), new PrnSchedule()),
        };

        DailyConsumption.RateOn(new DateOnly(2026, 3, 15), schedule).Should().Be(0m);
    }

    [Fact]
    public void RateOn_slots_still_win_over_non_fixed_schedule()
    {
        // A1 §3.1: slots take precedence when present, whatever the
        // schedule kind. Documented for A1; slot × schedule mixing is
        // out of scope for the projection engine.
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(new DateOnly(2026, 3, 1),
                new CyclicSchedule(3, 2, 5m)),
        };
        var slots = new[]
        {
            new MedicationAdministrationSlot
            {
                MedicineId = DomainFactory.MedicineId,
                Dose = 2m,
            },
        };

        DailyConsumption.RateOn(new DateOnly(2026, 3, 4), schedule, slots).Should().Be(2m);
    }

    [Fact]
    public void RateOn_versioned_history_switches_schedule_kind_mid_therapy()
    {
        var schedule = new[]
        {
            DomainFactory.Schedule(new DateOnly(2026, 3, 1), 1m, 2), // FixedDaily 2/day
            DomainFactory.ScheduleFor(new DateOnly(2026, 3, 10), new PrnSchedule()),
        };

        DailyConsumption.RateOn(new DateOnly(2026, 3, 5),  schedule).Should().Be(2m);
        DailyConsumption.RateOn(new DateOnly(2026, 3, 15), schedule).Should().Be(0m);
    }
}
