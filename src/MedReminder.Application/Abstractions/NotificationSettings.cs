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

    // Optional secondary recipient (caregiver) for this profile
    // (A3, docs/analysis/ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md §3).
    // When non-empty, the same emails delivered to ToAddress are also
    // delivered to this address, as a second To recipient in the same
    // MIME message. Empty (the default) means no caregiver configured
    // and the send path behaves exactly as before A3.
    public string CaregiverAddress { get; set; } = string.Empty;
}
