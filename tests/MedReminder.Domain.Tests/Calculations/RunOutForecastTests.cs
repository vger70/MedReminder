using FluentAssertions;
using MedReminder.Domain.Calculations;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

public class RunOutForecastTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);

    [Fact]
    public void Example_from_spec_section_7_18_by_2_gives_9_days()
    {
        var result = RunOutForecast.Compute(Today, currentStock: 18m, dailyRate: 2m, isSuspendedToday: false);

        result.DaysRemaining.Should().Be(9);
        result.EstimatedRunOutDate.Should().Be(new DateOnly(2026, 9, 22));
    }

    [Fact]
    public void Thirty_by_two_gives_fifteen_days()
    {
        var result = RunOutForecast.Compute(Today, 30m, 2m, false);

        result.DaysRemaining.Should().Be(15);
        result.EstimatedRunOutDate.Should().Be(Today.AddDays(15));
    }

    [Fact]
    public void Fractional_result_is_floored()
    {
        // 19/2 = 9.5 -> 9 days (spec §7 uses the effective
        // consumption, so the floor avoids "gifting" a day the
        // calculation does not support).
        var result = RunOutForecast.Compute(Today, 19m, 2m, false);

        result.DaysRemaining.Should().Be(9);
        result.EstimatedRunOutDate.Should().Be(Today.AddDays(9));
    }

    [Fact]
    public void Zero_stock_returns_zero_days_and_today_as_eta()
    {
        var result = RunOutForecast.Compute(Today, 0m, 2m, false);

        result.DaysRemaining.Should().Be(0);
        result.EstimatedRunOutDate.Should().Be(Today);
    }

    [Fact]
    public void Zero_rate_returns_null_result()
    {
        var result = RunOutForecast.Compute(Today, 20m, 0m, false);

        result.DaysRemaining.Should().BeNull();
        result.EstimatedRunOutDate.Should().BeNull();
    }

    [Fact]
    public void Suspended_today_returns_null_result()
    {
        var result = RunOutForecast.Compute(Today, 20m, 2m, isSuspendedToday: true);

        result.DaysRemaining.Should().BeNull();
        result.EstimatedRunOutDate.Should().BeNull();
    }

    [Fact]
    public void Negative_stock_after_clamping_treated_as_zero()
    {
        // MedicineStock.Current clampa a 0; qui ci difendiamo anche se un
        // valore negativo dovesse trapelare da un altro percorso.
        var result = RunOutForecast.Compute(Today, -5m, 2m, false);

        result.DaysRemaining.Should().Be(0);
        result.EstimatedRunOutDate.Should().Be(Today);
    }

    [Fact]
    public void Leap_year_boundary_29_february_is_a_valid_eta_date()
    {
        var feb28LeapYear = new DateOnly(2028, 2, 28);
        var result = RunOutForecast.Compute(feb28LeapYear, currentStock: 2m, dailyRate: 1m, isSuspendedToday: false);

        result.DaysRemaining.Should().Be(2);
        result.EstimatedRunOutDate.Should().Be(new DateOnly(2028, 3, 1));
    }

    [Fact]
    public void Leap_year_starting_on_28_feb_with_one_day_remaining_reaches_29_feb()
    {
        var feb28LeapYear = new DateOnly(2028, 2, 28);
        var result = RunOutForecast.Compute(feb28LeapYear, 1m, 1m, false);

        result.DaysRemaining.Should().Be(1);
        result.EstimatedRunOutDate.Should().Be(new DateOnly(2028, 2, 29));
    }

    [Fact]
    public void Non_integer_rate_produces_correct_floor()
    {
        // rate 1.5 su stock 10 -> 6.66... -> 6 giorni
        var result = RunOutForecast.Compute(Today, 10m, 1.5m, false);
        result.DaysRemaining.Should().Be(6);
    }
}
