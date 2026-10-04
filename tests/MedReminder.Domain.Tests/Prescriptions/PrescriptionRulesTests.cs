using FluentAssertions;
using MedReminder.Domain.Prescriptions;
using Xunit;

namespace MedReminder.Domain.Tests.Prescriptions;

// Prescription lifecycle (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2).
public class PrescriptionRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static Prescription P(DateOnly? requested = null, DateOnly? issued = null, DateOnly? until = null,
        DateOnly? collected = null, int? packages = null, string? code = null, int? dispensations = null) => new()
    {
        Dispensations = dispensations,
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
        P(requested: Today).StatusOn(Today, 0).Should().Be(PrescriptionStatus.Requested);
        P(issued: Today, until: Today.AddDays(29)).StatusOn(Today, 0).Should().Be(PrescriptionStatus.ToCollect);
        P(issued: Today).StatusOn(Today.AddDays(400), 0).Should().Be(PrescriptionStatus.ToCollect, "no end date known");
        P(issued: Today, until: Today.AddDays(29)).StatusOn(Today.AddDays(29), 0).Should().Be(PrescriptionStatus.ToCollect);
        P(issued: Today, until: Today.AddDays(29)).StatusOn(Today.AddDays(30), 0).Should().Be(PrescriptionStatus.Expired);
        P(issued: Today, until: Today, collected: Today).StatusOn(Today.AddDays(30), 0)
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
        PrescriptionRules.ReminderDue(P(issued: Today, until: until), until.AddDays(dayOffset), 0).Should().Be(due);
    }

    [Fact]
    public void No_reminder_without_an_issue_or_an_end_date_or_once_collected()
    {
        PrescriptionRules.ReminderDue(P(requested: Today, until: Today), Today, 0).Should().BeFalse();
        PrescriptionRules.ReminderDue(P(issued: Today), Today, 0).Should().BeFalse();
        PrescriptionRules.ReminderDue(P(issued: Today, until: Today, collected: Today), Today, 0).Should().BeFalse();
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

    private static PrescriptionDispensation[] Collected(Prescription p, params DateOnly[] days)
        => [.. days.Select(d => new PrescriptionDispensation { PrescriptionId = p.Id, MedicineId = p.MedicineId, CollectedOn = d })];

    [Theory]
    // Repeatable: 3 dispensations, valid 12 months from Today.
    [InlineData(3, 0, 0, PrescriptionStatus.ToCollect)]
    [InlineData(3, 2, 100, PrescriptionStatus.ToCollect)]
    [InlineData(3, 3, 100, PrescriptionStatus.Collected)]
    [InlineData(3, 3, 400, PrescriptionStatus.Collected)]
    [InlineData(3, 1, 365, PrescriptionStatus.Expired)]
    [InlineData(3, 1, 364, PrescriptionStatus.ToCollect)]
    // More recorded than allowed (two devices between syncs): collected.
    [InlineData(3, 4, 10, PrescriptionStatus.Collected)]
    public void A_repeatable_prescription_follows_its_dispensations(
        int allowed, int collected, int daysAfterIssue, PrescriptionStatus expected)
    {
        var p = P(issued: Today, until: PrescriptionRules.DefaultRepeatableValidUntil(Today), dispensations: allowed);
        p.StatusOn(Today.AddDays(daysAfterIssue), collected).Should().Be(expected);
    }

    [Fact]
    public void A_repeatable_prescription_not_issued_yet_is_requested()
    {
        P(requested: Today, dispensations: 12).StatusOn(Today, 0).Should().Be(PrescriptionStatus.Requested);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void One_dispensation_or_none_is_a_single_prescription(int? dispensations)
    {
        var p = P(issued: Today, until: Today.AddDays(29), dispensations: dispensations);
        p.IsRepeatable.Should().BeFalse();
        p.StatusOn(Today, dispensationsCollected: 5).Should().Be(PrescriptionStatus.ToCollect, "only CollectedOn counts");
        PrescriptionRules.DispensationsLeft(p, 0).Should().Be(1);
        p.CollectedOn = Today;
        p.StatusOn(Today, 0).Should().Be(PrescriptionStatus.Collected);
        PrescriptionRules.DispensationsLeft(p, 0).Should().Be(0);
    }

    [Fact]
    public void The_dispensations_left_never_go_below_zero()
    {
        var p = P(issued: Today, dispensations: 12);
        PrescriptionRules.DispensationsLeft(p, 0).Should().Be(12);
        PrescriptionRules.DispensationsLeft(p, Collected(p, Today, Today.AddDays(30))).Should().Be(10);
        PrescriptionRules.DispensationsLeft(p, 13).Should().Be(0);
    }

    [Fact]
    public void The_default_repeatable_validity_is_twelve_months_counting_the_issue_day()
    {
        PrescriptionRules.DefaultRepeatableValidUntil(new DateOnly(2026, 10, 1)).Should().Be(new DateOnly(2027, 9, 30));
    }

    [Fact]
    public void The_reminder_of_a_repeatable_prescription_needs_dispensations_left()
    {
        var until = Today.AddDays(10);
        var p = P(issued: Today, until: until, dispensations: 3);
        PrescriptionRules.ReminderDue(p, until, dispensationsCollected: 2).Should().BeTrue();
        PrescriptionRules.ReminderDue(p, until, dispensationsCollected: 3).Should().BeFalse();
        PrescriptionRules.ReminderDue(p, until.AddDays(-4), dispensationsCollected: 0).Should().BeFalse();
    }

    [Fact]
    public void Inconsistent_repeatable_entries_are_rejected()
    {
        var p = P(issued: Today, until: Today.AddDays(90), dispensations: 3);
        PrescriptionRules.Validate(p, Collected(p, Today, Today.AddDays(30))).Should().BeNull();
        PrescriptionRules.Validate(p, Collected(p, Today.AddDays(-1))).Should().Be(PrescriptionError.DispensationBeforeIssued);
        PrescriptionRules.Validate(p, Collected(p, Today.AddDays(91))).Should().Be(PrescriptionError.DispensationAfterValidity);
        PrescriptionRules.Validate(p, Collected(p, Today, Today, Today, Today))
            .Should().Be(PrescriptionError.TooManyDispensations);
        var badPackages = Collected(p, Today);
        badPackages[0].Packages = 0;
        PrescriptionRules.Validate(p, badPackages).Should().Be(PrescriptionError.Packages);

        PrescriptionRules.Validate(P(issued: Today, dispensations: 0)).Should().Be(PrescriptionError.Dispensations);
        PrescriptionRules.Validate(P(issued: Today, dispensations: PrescriptionRules.MaxDispensations + 1))
            .Should().Be(PrescriptionError.Dispensations);
        PrescriptionRules.Validate(P(issued: Today, dispensations: PrescriptionRules.MaxDispensations)).Should().BeNull();
        PrescriptionRules.Validate(P(issued: Today, collected: Today, dispensations: 2))
            .Should().Be(PrescriptionError.CollectedOnRepeatable);

        var single = P(issued: Today, dispensations: 1);
        PrescriptionRules.Validate(single, Collected(single, Today)).Should().Be(PrescriptionError.DispensationsOnSingle);
    }

    [Fact]
    public void Dispensations_passed_as_a_count_are_counted_but_not_checked()
    {
        var p = P(issued: Today, until: Today.AddDays(30), dispensations: 2);
        PrescriptionRules.Validate(p, Collected(p, Today), otherDispensations: 1).Should().BeNull();
        PrescriptionRules.Validate(p, Collected(p, Today), otherDispensations: 2)
            .Should().Be(PrescriptionError.TooManyDispensations);
        PrescriptionRules.Validate(P(dispensations: 2), otherDispensations: 1).Should().BeNull();
        PrescriptionRules.Validate(P(issued: Today), otherDispensations: 1)
            .Should().Be(PrescriptionError.DispensationsOnSingle);
    }

    [Fact]
    public void A_dispensation_counts_as_a_date()
    {
        var p = P(dispensations: 2);
        PrescriptionRules.Validate(p).Should().Be(PrescriptionError.NoDate);
        PrescriptionRules.Validate(p, Collected(p, Today)).Should().BeNull();
    }
}
