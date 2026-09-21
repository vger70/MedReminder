using FluentAssertions;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Domain.Tests.Medicines;

// A5: the opt-in for a dose-time reminder is only meaningful when the
// medicine has at least one timed slot AND has stock on hand. The
// domain helper is the single source of truth the UI reuses to clamp
// the checkbox (ANALYSIS-A5 §5.1).
public class MedicineDoseReminderTests
{
    [Fact]
    public void Can_remind_when_timed_slot_and_stock_present()
    {
        Medicine.CanRemindOnDose(hasTimedSlot: true, currentStock: 10m)
            .Should().BeTrue();
    }

    [Fact]
    public void Cannot_remind_without_a_timed_slot()
    {
        Medicine.CanRemindOnDose(hasTimedSlot: false, currentStock: 10m)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Cannot_remind_when_stock_not_positive(decimal stock)
    {
        Medicine.CanRemindOnDose(hasTimedSlot: true, currentStock: stock)
            .Should().BeFalse();
    }

    [Fact]
    public void Cannot_remind_when_neither_condition_holds()
    {
        Medicine.CanRemindOnDose(hasTimedSlot: false, currentStock: 0m)
            .Should().BeFalse();
    }
}
