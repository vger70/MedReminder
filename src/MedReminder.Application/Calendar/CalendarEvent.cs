namespace MedReminder.Application.Calendar;

// An all-day event of the calendar export (docs/notes/
// EVOLUTION-PROPOSALS-2.md §3.7). Uid is stable per subject and kind, so
// importing a newer export updates the event instead of adding a copy.
public sealed record CalendarEvent(string Uid, DateOnly Date, string Summary, string? Description = null);
