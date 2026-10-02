namespace MedReminder.Domain.Deadlines;

// Rules of the administrative deadlines (docs/notes/
// EVOLUTION-PROPOSALS-2.md §3.6).
public static class DeadlineRules
{
    public const int DefaultLeadDays = 14;

    public const int MaxLeadDays = 180;

    public const int MaxRepeatMonths = 120;

    public const int MaxLabelLength = 80;

    // A deadline not done whose reminder window has started: from
    // LeadDays before DueOn on, overdue days included.
    public static bool ReminderDue(Deadline deadline, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(deadline);
        return deadline.DoneOn is null && today >= deadline.DueOn.AddDays(-deadline.LeadDays);
    }

    // Marks the deadline done on a day. A recurring deadline moves to its
    // next date, counted from the date it was due (not from the day it
    // was done), so a late renewal does not shift the whole series.
    public static void Complete(Deadline deadline, DateOnly doneOn)
    {
        ArgumentNullException.ThrowIfNull(deadline);
        if (deadline.RepeatMonths is { } months)
        {
            deadline.DueOn = deadline.DueOn.AddMonths(months);
            deadline.DoneOn = null;
        }
        else
        {
            deadline.DoneOn = doneOn;
        }
    }

    // Null when the entry is consistent, else the reason.
    public static DeadlineError? Validate(Deadline deadline)
    {
        ArgumentNullException.ThrowIfNull(deadline);
        if (deadline.Kind == DeadlineKind.Other && string.IsNullOrWhiteSpace(deadline.Label))
            return DeadlineError.NoLabel;
        if (deadline.Label is { Length: > MaxLabelLength })
            return DeadlineError.Label;
        if (deadline.LeadDays < 0 || deadline.LeadDays > MaxLeadDays)
            return DeadlineError.LeadDays;
        if (deadline.RepeatMonths is { } months && (months < 1 || months > MaxRepeatMonths))
            return DeadlineError.RepeatMonths;
        if (deadline.DoneOn is not null && deadline.RepeatMonths is not null)
            return DeadlineError.DoneRecurring;
        return null;
    }
}

public enum DeadlineError
{
    NoLabel,
    Label,
    LeadDays,
    RepeatMonths,
    DoneRecurring,
}
