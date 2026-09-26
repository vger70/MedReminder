namespace MedReminder.Application.Notifications;

// ExplicitRecipient is null for the automated notifications, which go
// to the profile's ToAddress (plus the optional caregiver). When set,
// the message is an interactive, user-confirmed send to exactly that
// address (prescription request to the doctor): the adapter skips the
// profile recipients, the retry decorator does not back off, and no
// component logs the address.
public sealed record EmailMessage(string Subject, string Body, string? ExplicitRecipient = null);
