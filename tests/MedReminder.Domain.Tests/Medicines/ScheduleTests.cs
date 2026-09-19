using FluentAssertions;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Domain.Tests.Medicines;

public class ScheduleTests
{
    private static readonly DateOnly Anchor = new(2026, 1, 5); // Monday

    // ---------- FixedDailySchedule ---------------------------------

    [Fact]
    public void Fixed_daily_returns_dose_times_administrations_every_day()
    {
        var schedule = new FixedDailySchedule(1.5m, 2);
        schedule.RateOn(Anchor, Anchor).Should().Be(3m);
        schedule.RateOn(Anchor.AddDays(500), Anchor).Should().Be(3m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Fixed_daily_rejects_non_positive_dose(int dose)
    {
        Action act = () => _ = new FixedDailySchedule(dose, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Fixed_daily_rejects_non_positive_frequency(int frequency)
    {
        Action act = () => _ = new FixedDailySchedule(1m, frequency);
        act.Should().Throw<ArgumentException>();
    }

    // ---------- WeeklySchedule -------------------------------------

    [Fact]
    public void Weekly_uses_monday_as_index_zero()
    {
        // Mon 1, Tue 2, Wed 3, Thu 4, Fri 5, Sat 6, Sun 7
        var schedule = new WeeklySchedule(new[] { 1m, 2m, 3m, 4m, 5m, 6m, 7m });

        var monday    = new DateOnly(2026, 1, 5);
        var tuesday   = new DateOnly(2026, 1, 6);
        var wednesday = new DateOnly(2026, 1, 7);
        var thursday  = new DateOnly(2026, 1, 8);
        var friday    = new DateOnly(2026, 1, 9);
        var saturday  = new DateOnly(2026, 1, 10);
        var sunday    = new DateOnly(2026, 1, 11);

        schedule.RateOn(monday,    Anchor).Should().Be(1m);
        schedule.RateOn(tuesday,   Anchor).Should().Be(2m);
        schedule.RateOn(wednesday, Anchor).Should().Be(3m);
        schedule.RateOn(thursday,  Anchor).Should().Be(4m);
        schedule.RateOn(friday,    Anchor).Should().Be(5m);
        schedule.RateOn(saturday,  Anchor).Should().Be(6m);
        schedule.RateOn(sunday,    Anchor).Should().Be(7m);
    }

    [Fact]
    public void Weekly_rejects_arrays_that_are_not_exactly_seven_long()
    {
        Action six = () => _ = new WeeklySchedule(new[] { 1m, 1m, 1m, 1m, 1m, 1m });
        Action eight = () => _ = new WeeklySchedule(new[] { 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m });
        six.Should().Throw<ArgumentException>();
        eight.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Weekly_rejects_negative_quantities()
    {
        Action act = () => _ = new WeeklySchedule(new[] { 1m, 1m, -1m, 1m, 1m, 1m, 1m });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Weekly_rejects_all_zero_pattern()
    {
        Action act = () => _ = new WeeklySchedule(new[] { 0m, 0m, 0m, 0m, 0m, 0m, 0m });
        act.Should().Throw<ArgumentException>();
    }

    // ---------- CyclicSchedule -------------------------------------

    [Fact]
    public void Cyclic_returns_quantity_on_the_first_on_days_and_zero_afterwards()
    {
        var schedule = new CyclicSchedule(onDays: 3, offDays: 2, quantityPerOnDay: 1m);
        // Anchor + 0..2 => on-days (quantity 1). +3..+4 => off. +5..+7 => next on-cycle.
        for (var d = 0; d < 3; d++)
        {
            schedule.RateOn(Anchor.AddDays(d), Anchor).Should().Be(1m,
                because: $"day+{d} sits in the first on-window");
        }
        schedule.RateOn(Anchor.AddDays(3), Anchor).Should().Be(0m);
        schedule.RateOn(Anchor.AddDays(4), Anchor).Should().Be(0m);
        for (var d = 5; d < 8; d++)
        {
            schedule.RateOn(Anchor.AddDays(d), Anchor).Should().Be(1m,
                because: $"day+{d} sits in the second on-window");
        }
    }

    [Fact]
    public void Cyclic_returns_zero_for_days_before_the_anchor()
    {
        var schedule = new CyclicSchedule(21, 7, 1m);
        schedule.RateOn(Anchor.AddDays(-1), Anchor).Should().Be(0m);
    }

    [Fact]
    public void Cyclic_rejects_invalid_parameters()
    {
        Action zeroOn = () => _ = new CyclicSchedule(0, 7, 1m);
        Action negOff = () => _ = new CyclicSchedule(1, -1, 1m);
        Action zeroQty = () => _ = new CyclicSchedule(1, 1, 0m);
        zeroOn.Should().Throw<ArgumentException>();
        negOff.Should().Throw<ArgumentException>();
        zeroQty.Should().Throw<ArgumentException>();
    }

    // ---------- TaperingSchedule -----------------------------------

    [Fact]
    public void Tapering_descending_clamps_at_end_dose()
    {
        // 4 -> 0.5 mg, step 0.5, every 7 days. Reaches 0.5 after 7 steps
        // (4, 3.5, 3, 2.5, 2, 1.5, 1, 0.5), then holds at 0.5.
        var schedule = new TaperingSchedule(startDose: 4m, endDose: 0.5m, step: 0.5m, intervalDays: 7);

        schedule.RateOn(Anchor,                    Anchor).Should().Be(4m);
        schedule.RateOn(Anchor.AddDays(6),         Anchor).Should().Be(4m);   // still in the first interval
        schedule.RateOn(Anchor.AddDays(7),         Anchor).Should().Be(3.5m);
        schedule.RateOn(Anchor.AddDays(7 * 7),     Anchor).Should().Be(0.5m); // reached endDose
        schedule.RateOn(Anchor.AddDays(7 * 20),    Anchor).Should().Be(0.5m); // clamp holds
    }

    [Fact]
    public void Tapering_ascending_clamps_at_end_dose()
    {
        var schedule = new TaperingSchedule(startDose: 0.5m, endDose: 2m, step: 0.5m, intervalDays: 7);
        schedule.RateOn(Anchor,                Anchor).Should().Be(0.5m);
        schedule.RateOn(Anchor.AddDays(7),     Anchor).Should().Be(1m);
        schedule.RateOn(Anchor.AddDays(7 * 3), Anchor).Should().Be(2m);
        schedule.RateOn(Anchor.AddDays(7 * 9), Anchor).Should().Be(2m);   // clamp holds
    }

    [Fact]
    public void Tapering_never_returns_a_negative_rate()
    {
        // A pathological configuration where StartDose - Step * N goes
        // negative before EndDose is reached would still be clamped at
        // EndDose. Guard the projection against negative values just in
        // case the invariants are ever relaxed.
        var schedule = new TaperingSchedule(startDose: 1m, endDose: 0m, step: 0.5m, intervalDays: 1);
        schedule.RateOn(Anchor.AddDays(1000), Anchor).Should().Be(0m);
    }

    [Fact]
    public void Tapering_returns_zero_before_anchor()
    {
        var schedule = new TaperingSchedule(4m, 0.5m, 0.5m, 7);
        schedule.RateOn(Anchor.AddDays(-1), Anchor).Should().Be(0m);
    }

    [Fact]
    public void Tapering_rejects_equal_start_and_end()
    {
        Action act = () => _ = new TaperingSchedule(1m, 1m, 0.5m, 7);
        act.Should().Throw<ArgumentException>();
    }

    // ---------- PrnSchedule ----------------------------------------

    [Fact]
    public void Prn_always_returns_zero()
    {
        var schedule = new PrnSchedule();
        schedule.RateOn(Anchor, Anchor).Should().Be(0m);
        schedule.RateOn(Anchor.AddDays(1000), Anchor).Should().Be(0m);
        schedule.RateOn(Anchor.AddDays(-500), Anchor).Should().Be(0m);
    }
}
