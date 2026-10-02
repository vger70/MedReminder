using FluentAssertions;
using MedReminder.Domain.Deadlines;
using Xunit;

namespace MedReminder.Domain.Tests.Deadlines;

public class DeadlineRulesTests
{
    private static readonly DateOnly Due = new(2026, 10, 31);

    private static Deadline Plan(int leadDays = 14, int? repeatMonths = null, DateOnly? doneOn = null) => new()
    {
        Kind = DeadlineKind.TherapeuticPlan,
        DueOn = Due,
        LeadDays = leadDays,
        RepeatMonths = repeatMonths,
        DoneOn = doneOn,
    };

    [Theory]
    [InlineData(-15, DeadlineStatus.Upcoming)]
    [InlineData(-14, DeadlineStatus.DueSoon)]
    [InlineData(0, DeadlineStatus.DueSoon)]
    [InlineData(1, DeadlineStatus.Overdue)]
    public void The_status_follows_the_date_and_the_notice(int daysFromDue, DeadlineStatus expected)
    {
        Plan().StatusOn(Due.AddDays(daysFromDue)).Should().Be(expected);
        DeadlineRules.ReminderDue(Plan(), Due.AddDays(daysFromDue))
            .Should().Be(expected != DeadlineStatus.Upcoming);
    }

    [Fact]
    public void A_done_deadline_is_done_and_not_reminded()
    {
        var deadline = Plan(doneOn: Due.AddDays(-3));

        deadline.StatusOn(Due.AddDays(5)).Should().Be(DeadlineStatus.Done);
        DeadlineRules.ReminderDue(deadline, Due).Should().BeFalse();
    }

    [Fact]
    public void A_zero_notice_reminds_on_the_day()
    {
        DeadlineRules.ReminderDue(Plan(leadDays: 0), Due.AddDays(-1)).Should().BeFalse();
        DeadlineRules.ReminderDue(Plan(leadDays: 0), Due).Should().BeTrue();
    }

    [Fact]
    public void Completing_a_one_off_deadline_closes_it()
    {
        var deadline = Plan();

        DeadlineRules.Complete(deadline, Due.AddDays(2));

        deadline.DoneOn.Should().Be(Due.AddDays(2));
        deadline.DueOn.Should().Be(Due);
    }

    [Fact]
    public void Completing_a_recurring_deadline_moves_it_from_its_due_date()
    {
        var deadline = Plan(repeatMonths: 6);

        DeadlineRules.Complete(deadline, Due.AddDays(20));

        deadline.DoneOn.Should().BeNull();
        deadline.DueOn.Should().Be(new DateOnly(2027, 4, 30), "month end is clamped, not the day it was done");
    }

    [Theory]
    [InlineData(DeadlineKind.Other, null, 14, null, null, DeadlineError.NoLabel)]
    [InlineData(DeadlineKind.Other, " ", 14, null, null, DeadlineError.NoLabel)]
    [InlineData(DeadlineKind.CheckUp, null, -1, null, null, DeadlineError.LeadDays)]
    [InlineData(DeadlineKind.CheckUp, null, DeadlineRules.MaxLeadDays + 1, null, null, DeadlineError.LeadDays)]
    [InlineData(DeadlineKind.CheckUp, null, 14, 0, null, DeadlineError.RepeatMonths)]
    [InlineData(DeadlineKind.CheckUp, null, 14, DeadlineRules.MaxRepeatMonths + 1, null, DeadlineError.RepeatMonths)]
    [InlineData(DeadlineKind.CheckUp, null, 14, 12, true, DeadlineError.DoneRecurring)]
    public void Inconsistent_entries_are_refused(
        DeadlineKind kind, string? label, int leadDays, int? repeatMonths, bool? done, DeadlineError expected)
    {
        var deadline = new Deadline
        {
            Kind = kind,
            Label = label,
            DueOn = Due,
            LeadDays = leadDays,
            RepeatMonths = repeatMonths,
            DoneOn = done == true ? Due : null,
        };

        DeadlineRules.Validate(deadline).Should().Be(expected);
    }

    [Fact]
    public void A_long_label_is_refused_and_a_plain_entry_is_accepted()
    {
        Plan().Should().Match<Deadline>(d => DeadlineRules.Validate(d) == null);
        var tooLong = Plan();
        tooLong.Label = new string('x', DeadlineRules.MaxLabelLength + 1);
        DeadlineRules.Validate(tooLong).Should().Be(DeadlineError.Label);
    }
}
