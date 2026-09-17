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
}
