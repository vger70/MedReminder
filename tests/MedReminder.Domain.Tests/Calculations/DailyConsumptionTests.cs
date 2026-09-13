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
}
