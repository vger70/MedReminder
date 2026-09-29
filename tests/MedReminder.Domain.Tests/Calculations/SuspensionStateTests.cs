using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Tests.Support;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

public class SuspensionStateTests
{
    [Fact]
    public void IsSuspendedOn_returns_false_with_no_suspensions()
    {
        SuspensionState.IsSuspendedOn(
            new DateOnly(2026, 3, 1),
            Array.Empty<MedicationSuspension>())
            .Should().BeFalse();
    }

    [Fact]
    public void IsSuspendedOn_returns_false_for_day_before_start()
    {
        var s = new[] { DomainFactory.Suspension(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 10)) };
        SuspensionState.IsSuspendedOn(new DateOnly(2026, 3, 4), s).Should().BeFalse();
    }

    [Fact]
    public void IsSuspendedOn_returns_true_on_start_date_inclusive()
    {
        var s = new[] { DomainFactory.Suspension(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 10)) };
        SuspensionState.IsSuspendedOn(new DateOnly(2026, 3, 5), s).Should().BeTrue();
    }

    [Fact]
    public void IsSuspendedOn_returns_true_on_end_date_inclusive()
    {
        var s = new[] { DomainFactory.Suspension(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 10)) };
        SuspensionState.IsSuspendedOn(new DateOnly(2026, 3, 10), s).Should().BeTrue();
    }

    [Fact]
    public void IsSuspendedOn_returns_false_after_end_date()
    {
        var s = new[] { DomainFactory.Suspension(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 10)) };
        SuspensionState.IsSuspendedOn(new DateOnly(2026, 3, 11), s).Should().BeFalse();
    }

    [Fact]
    public void IsSuspendedOn_open_suspension_covers_all_days_from_start()
    {
        var s = new[] { DomainFactory.Suspension(new DateOnly(2026, 3, 5)) };
        SuspensionState.IsSuspendedOn(new DateOnly(2026, 3, 5), s).Should().BeTrue();
        SuspensionState.IsSuspendedOn(new DateOnly(2027, 1, 1), s).Should().BeTrue();
    }

    [Fact]
    public void IsSuspendedOn_returns_true_when_any_of_multiple_periods_matches()
    {
        var s = new[]
        {
            DomainFactory.Suspension(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3)),
            DomainFactory.Suspension(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 10)),
        };
        SuspensionState.IsSuspendedOn(new DateOnly(2026, 3, 7), s).Should().BeTrue();
        SuspensionState.IsSuspendedOn(new DateOnly(2026, 2, 1), s).Should().BeFalse();
    }
}
