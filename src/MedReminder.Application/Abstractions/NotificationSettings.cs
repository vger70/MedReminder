namespace MedReminder.Application.Abstractions;

// Per-profile notification settings
// (docs/ANALYSIS-MULTI-USER.md §7.1). Introduced as a POCO in
// Increment 15a so downstream sub-increments (15c) can wire it into
// IOptionsMonitor without another Application-layer change. Not yet
// consumed by MailKitEmailNotificationService — ToAddress still
// lives on SmtpSettings until 15c completes the split.
public sealed class NotificationSettings
{
    public const string SectionName = "Notifications";

    // Recipient address for this profile's automated emails. When
    // empty, the profile has no configured recipient and the email
    // notification is skipped by the caller.
    public string ToAddress { get; set; } = string.Empty;
}
