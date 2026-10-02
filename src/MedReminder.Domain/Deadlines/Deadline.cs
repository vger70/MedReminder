using MedReminder.Domain.Notifications;

namespace MedReminder.Domain.Deadlines;

// An administrative deadline the user wants to be reminded of
// (docs/notes/EVOLUTION-PROPOSALS-2.md §3.6): the renewal of a
// therapeutic plan or of an exemption, a periodic check-up. Optionally
// tied to a medicine, optionally recurring every RepeatMonths months.
// A date reminder with no clinical content; the label is free text and
// is never logged. Validity periods differ by plan and region, so every
// date is entered by the user: there is no regulatory default.
public sealed class Deadline
{
    public Guid Id { get; init; } = Guid.NewGuid();

    // Null for a deadline of the profile, not of one medicine.
    public Guid? MedicineId { get; set; }

    public DeadlineKind Kind { get; set; }

    // What the deadline is about, in the user's words. Required for
    // DeadlineKind.Other, optional otherwise.
    public string? Label { get; set; }

    public DateOnly DueOn { get; set; }

    // Days before DueOn when the reminder is due.
    public int LeadDays { get; set; } = DeadlineRules.DefaultLeadDays;

    // Null for a one-off deadline.
    public int? RepeatMonths { get; set; }

    public NotificationChannels Channels { get; set; } = NotificationChannels.Both;

    // Set when a one-off deadline is done; a recurring one moves to its
    // next date instead (DeadlineRules.Complete).
    public DateOnly? DoneOn { get; set; }

    public DateTimeOffset RecordedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DeadlineStatus StatusOn(DateOnly today)
    {
        if (DoneOn is not null) return DeadlineStatus.Done;
        if (today > DueOn) return DeadlineStatus.Overdue;
        return today >= DueOn.AddDays(-LeadDays) ? DeadlineStatus.DueSoon : DeadlineStatus.Upcoming;
    }
}

// Wire names in the sync format and in exports: do not rename members.
public enum DeadlineKind
{
    TherapeuticPlan,
    ExemptionRenewal,
    CheckUp,
    Other,
}

// Order matters: lists show the deadlines to act on first.
public enum DeadlineStatus
{
    Overdue,
    DueSoon,
    Upcoming,
    Done,
}
