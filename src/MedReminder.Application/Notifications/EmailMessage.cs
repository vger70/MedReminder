using MedReminder.Application.Calendar;

namespace MedReminder.Application.Notifications;

// ExplicitRecipient is null for the automated notifications, which go
// to the profile's ToAddress (plus the optional caregiver). When set,
// the message is an interactive, user-confirmed send to exactly that
// address (prescription request to the doctor): the adapter skips the
// profile recipients, the retry decorator does not back off, and no
// component logs the address.
//
// CalendarEvent (docs/notes/EVOLUTION-PROPOSALS-2.md §3.7): an event the
// adapter attaches as an .ics file, with a generic title; null for none.
public sealed record EmailMessage(
    string Subject,
    string Body,
    string? ExplicitRecipient = null,
    CalendarEvent? CalendarEvent = null);
