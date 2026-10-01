using FluentAssertions;
using MedReminder.Domain.Prescriptions;
using Xunit;

namespace MedReminder.Domain.Tests.Prescriptions;

// Prescription lifecycle (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2).
public class PrescriptionRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static Prescription P(DateOnly? requested = null, DateOnly? issued = null, DateOnly? until = null,
        DateOnly? collected = null, int? packages = null, string? code = null) => new()
    {
        MedicineId = Guid.NewGuid(),
        RequestedOn = requested,
        IssuedOn = issued,
        ValidUntil = until,
        CollectedOn = collected,
        Packages = packages,
        Code = code,
    };

    [Fact]
    public void The_status_follows_the_steps_recorded()
    {
        P(requested: Today).StatusOn(Today).Should().Be(PrescriptionStatus.Requested);
        P(issued: Today, until: Today.AddDays(29)).StatusOn(Today).Should().Be(PrescriptionStatus.ToCollect);
        P(issued: Today).StatusOn(Today.AddDays(400)).Should().Be(PrescriptionStatus.ToCollect, "no end date known");
        P(issued: Today, until: Today.AddDays(29)).StatusOn(Today.AddDays(29)).Should().Be(PrescriptionStatus.ToCollect);
        P(issued: Today, until: Today.AddDays(29)).StatusOn(Today.AddDays(30)).Should().Be(PrescriptionStatus.Expired);
        P(issued: Today, until: Today, collected: Today).StatusOn(Today.AddDays(30))
            .Should().Be(PrescriptionStatus.Collected);
    }

    [Fact]
    public void The_default_validity_counts_the_issue_day()
    {
        PrescriptionRules.DefaultValidUntil(Today).Should().Be(Today.AddDays(PrescriptionRules.DefaultValidityDays - 1));
    }

    [Theory]
    [InlineData(-4, false)]
    [InlineData(-3, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void The_reminder_is_due_in_the_last_days_of_validity(int dayOffset, bool due)
    {
        var until = Today.AddDays(10);
        PrescriptionRules.ReminderDue(P(issued: Today, until: until), until.AddDays(dayOffset)).Should().Be(due);
    }

    [Fact]
    public void No_reminder_without_an_issue_or_an_end_date_or_once_collected()
    {
        PrescriptionRules.ReminderDue(P(requested: Today, until: Today), Today).Should().BeFalse();
        PrescriptionRules.ReminderDue(P(issued: Today), Today).Should().BeFalse();
        PrescriptionRules.ReminderDue(P(issued: Today, until: Today, collected: Today), Today).Should().BeFalse();
    }

    [Fact]
    public void Inconsistent_entries_are_rejected()
    {
        PrescriptionRules.Validate(P()).Should().Be(PrescriptionError.NoDate);
        PrescriptionRules.Validate(P(requested: Today, issued: Today.AddDays(-1)))
            .Should().Be(PrescriptionError.IssuedBeforeRequested);
        PrescriptionRules.Validate(P(issued: Today, until: Today.AddDays(-1)))
            .Should().Be(PrescriptionError.ValidBeforeIssued);
        PrescriptionRules.Validate(P(issued: Today, collected: Today.AddDays(-1)))
            .Should().Be(PrescriptionError.CollectedBeforeIssued);
        PrescriptionRules.Validate(P(issued: Today, packages: 0)).Should().Be(PrescriptionError.Packages);
        PrescriptionRules.Validate(P(issued: Today, code: new string('x', 65))).Should().Be(PrescriptionError.Code);
        PrescriptionRules.Validate(P(requested: Today, issued: Today, until: Today, collected: Today, packages: 2,
            code: "0123456789")).Should().BeNull();
    }
}
