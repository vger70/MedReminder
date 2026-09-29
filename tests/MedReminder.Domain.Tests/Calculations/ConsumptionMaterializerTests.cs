using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Tests.Support;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

public class ConsumptionMaterializerTests
{
    [Fact]
    public void Plan_returns_empty_when_range_is_inverted()
    {
        var medicine = DomainFactory.Medicine(startDate: new DateOnly(2026, 1, 1));
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 1, 1), 1m, 2) };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            rangeStartInclusive: new DateOnly(2026, 3, 10),
            rangeEndInclusive: new DateOnly(2026, 3, 5),
            schedule,
            Array.Empty<MedicationSuspension>());

        plan.Should().BeEmpty();
    }

    [Fact]
    public void Plan_produces_one_entry_per_day_without_suspensions()
    {
        var medicine = DomainFactory.Medicine(startDate: new DateOnly(2026, 3, 1));
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 3, 1), 1m, 2) };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            rangeStartInclusive: new DateOnly(2026, 3, 1),
            rangeEndInclusive: new DateOnly(2026, 3, 3),
            schedule,
            Array.Empty<MedicationSuspension>());

        plan.Should().HaveCount(3);
        plan.Select(p => p.Day).Should().Equal(
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 2),
            new DateOnly(2026, 3, 3));
        plan.Should().OnlyContain(p => p.Quantity == 2m);
    }

    [Fact]
    public void Plan_skips_suspended_days()
    {
        var medicine = DomainFactory.Medicine(startDate: new DateOnly(2026, 3, 1));
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 3, 1), 1m, 2) };
        var suspensions = new[]
        {
            DomainFactory.Suspension(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3)),
        };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 5),
            schedule,
            suspensions);

        plan.Select(p => p.Day).Should().Equal(
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 4),
            new DateOnly(2026, 3, 5));
    }

    [Fact]
    public void Plan_clips_range_start_to_medicine_start_date()
    {
        var medicine = DomainFactory.Medicine(startDate: new DateOnly(2026, 3, 5));
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 3, 5), 1m, 2) };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            rangeStartInclusive: new DateOnly(2026, 3, 1),
            rangeEndInclusive: new DateOnly(2026, 3, 6),
            schedule,
            Array.Empty<MedicationSuspension>());

        plan.Select(p => p.Day).Should().Equal(
            new DateOnly(2026, 3, 5),
            new DateOnly(2026, 3, 6));
    }

    [Fact]
    public void Plan_clips_range_end_to_medicine_end_date()
    {
        var medicine = DomainFactory.Medicine(
            startDate: new DateOnly(2026, 3, 1),
            endDate: new DateOnly(2026, 3, 3));
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 3, 1), 1m, 2) };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 10),
            schedule,
            Array.Empty<MedicationSuspension>());

        plan.Select(p => p.Day).Should().Equal(
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 2),
            new DateOnly(2026, 3, 3));
    }

    [Fact]
    public void Plan_uses_versioned_schedule_when_rate_changes_mid_range()
    {
        var medicine = DomainFactory.Medicine(startDate: new DateOnly(2026, 3, 1));
        var schedule = new[]
        {
            DomainFactory.Schedule(new DateOnly(2026, 3, 1), 1m, 2),  // 2/gg fino al 2/3
            DomainFactory.Schedule(new DateOnly(2026, 3, 3), 1m, 3),  // 3/gg dal 3/3
        };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 4),
            schedule,
            Array.Empty<MedicationSuspension>());

        plan.Should().HaveCount(4);
        plan[0].Should().Be(new MaterializedConsumption(new DateOnly(2026, 3, 1), 2m));
        plan[1].Should().Be(new MaterializedConsumption(new DateOnly(2026, 3, 2), 2m));
        plan[2].Should().Be(new MaterializedConsumption(new DateOnly(2026, 3, 3), 3m));
        plan[3].Should().Be(new MaterializedConsumption(new DateOnly(2026, 3, 4), 3m));
    }

    [Fact]
    public void Plan_skips_days_when_daily_rate_is_zero()
    {
        // No schedule before Mar 3 -> rate 0 for the first two days.
        var medicine = DomainFactory.Medicine(startDate: new DateOnly(2026, 3, 1));
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 3, 3), 1m, 2) };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 4),
            schedule,
            Array.Empty<MedicationSuspension>());

        plan.Select(p => p.Day).Should().Equal(
            new DateOnly(2026, 3, 3),
            new DateOnly(2026, 3, 4));
    }

    [Fact]
    public void Plan_open_ended_suspension_suppresses_all_subsequent_days()
    {
        var medicine = DomainFactory.Medicine(startDate: new DateOnly(2026, 3, 1));
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 3, 1), 1m, 2) };
        var suspensions = new[] { DomainFactory.Suspension(new DateOnly(2026, 3, 3)) };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 10),
            schedule,
            suspensions);

        plan.Select(p => p.Day).Should().Equal(
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 2));
    }

    [Fact]
    public void Plan_single_day_range_is_emitted_when_all_conditions_hold()
    {
        var day = new DateOnly(2026, 3, 5);
        var medicine = DomainFactory.Medicine(startDate: new DateOnly(2026, 3, 1));
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 3, 1), 1m, 2) };

        var plan = ConsumptionMaterializer.Plan(medicine, day, day, schedule, Array.Empty<MedicationSuspension>());

        plan.Should().ContainSingle().Which.Should().Be(new MaterializedConsumption(day, 2m));
    }

    // ---------- A1 schedule kinds ----------------------------------

    [Fact]
    public void Plan_cyclic_schedule_only_emits_on_days_across_two_full_periods()
    {
        var effectiveFrom = new DateOnly(2026, 3, 1);
        var medicine = DomainFactory.Medicine(startDate: effectiveFrom);
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(effectiveFrom, new CyclicSchedule(onDays: 3, offDays: 2, quantityPerOnDay: 1m)),
        };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            effectiveFrom,
            effectiveFrom.AddDays(9),   // 10-day window = two full periods
            schedule,
            Array.Empty<MedicationSuspension>());

        plan.Select(p => p.Day).Should().Equal(
            effectiveFrom,
            effectiveFrom.AddDays(1),
            effectiveFrom.AddDays(2),
            // off: +3, +4
            effectiveFrom.AddDays(5),
            effectiveFrom.AddDays(6),
            effectiveFrom.AddDays(7));
            // off: +8, +9
        plan.Should().OnlyContain(p => p.Quantity == 1m);
    }

    [Fact]
    public void Plan_tapering_schedule_steps_down_across_intervals()
    {
        var effectiveFrom = new DateOnly(2026, 3, 1);
        var medicine = DomainFactory.Medicine(startDate: effectiveFrom);
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(effectiveFrom,
                new TaperingSchedule(startDose: 3m, endDose: 1m, step: 1m, intervalDays: 7)),
        };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            effectiveFrom,
            effectiveFrom.AddDays(20),   // 21 days = 3 intervals
            schedule,
            Array.Empty<MedicationSuspension>());

        plan.Should().HaveCount(21);
        // Day 0..6 → 3, day 7..13 → 2, day 14..20 → 1 (clamped at end).
        plan.Take(7).Should().OnlyContain(p => p.Quantity == 3m);
        plan.Skip(7).Take(7).Should().OnlyContain(p => p.Quantity == 2m);
        plan.Skip(14).Should().OnlyContain(p => p.Quantity == 1m);
    }

    [Fact]
    public void Plan_prn_schedule_emits_nothing()
    {
        var effectiveFrom = new DateOnly(2026, 3, 1);
        var medicine = DomainFactory.Medicine(startDate: effectiveFrom);
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(effectiveFrom, new PrnSchedule()),
        };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            effectiveFrom,
            effectiveFrom.AddDays(30),
            schedule,
            Array.Empty<MedicationSuspension>());

        plan.Should().BeEmpty();
    }

    [Fact]
    public void Plan_weekly_schedule_emits_only_positive_days()
    {
        // Warfarin-style: 1 mg Mon/Wed/Fri, 0.5 mg on the other days.
        var effectiveFrom = new DateOnly(2026, 3, 2);   // Monday
        var medicine = DomainFactory.Medicine(startDate: effectiveFrom);
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(effectiveFrom,
                new WeeklySchedule(new[] { 1m, 0.5m, 1m, 0.5m, 1m, 0.5m, 0.5m })),
        };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            effectiveFrom,
            effectiveFrom.AddDays(6),   // one full week
            schedule,
            Array.Empty<MedicationSuspension>());

        plan.Should().HaveCount(7);
        plan.Select(p => p.Quantity).Should().Equal(1m, 0.5m, 1m, 0.5m, 1m, 0.5m, 0.5m);
    }

    [Fact]
    public void Plan_stepped_tapering_bounded_course_materializes_each_stage_then_stops()
    {
        // 4/day for 7 days, 2/day for 7 days, 1/day for 14 days = 56 units.
        var effectiveFrom = new DateOnly(2026, 3, 1);
        var medicine = DomainFactory.Medicine(startDate: effectiveFrom);
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(effectiveFrom, new SteppedTaperingSchedule(new[]
            {
                new TaperStage(4m, 7),
                new TaperStage(2m, 7),
                new TaperStage(1m, 14),
            })),
        };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            effectiveFrom,
            effectiveFrom.AddDays(34),   // window extends past the 28-day course
            schedule,
            Array.Empty<MedicationSuspension>());

        // Nothing beyond day 28 (elapsed 27); the course is over.
        plan.Should().HaveCount(28);
        plan.Take(7).Should().OnlyContain(p => p.Quantity == 4m);
        plan.Skip(7).Take(7).Should().OnlyContain(p => p.Quantity == 2m);
        plan.Skip(14).Take(14).Should().OnlyContain(p => p.Quantity == 1m);
        plan.Sum(p => p.Quantity).Should().Be(56m);
    }

    [Fact]
    public void Plan_stepped_tapering_maintenance_keeps_the_last_dose_past_the_last_stage()
    {
        var effectiveFrom = new DateOnly(2026, 3, 1);
        var medicine = DomainFactory.Medicine(startDate: effectiveFrom);
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(effectiveFrom, new SteppedTaperingSchedule(
                new[]
                {
                    new TaperStage(4m, 7),
                    new TaperStage(2m, 7),
                    new TaperStage(1m, 14),
                },
                maintainLastDose: true)),
        };

        var plan = ConsumptionMaterializer.Plan(
            medicine,
            effectiveFrom,
            effectiveFrom.AddDays(34),   // 35 days
            schedule,
            Array.Empty<MedicationSuspension>());

        plan.Should().HaveCount(35);
        // Days 28..34 (elapsed 28..34) keep the maintenance dose of 1.
        plan.Skip(28).Should().OnlyContain(p => p.Quantity == 1m);
    }
}
