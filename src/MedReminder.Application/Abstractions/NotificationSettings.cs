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

    // Optional address of the profile's doctor, used only as the
    // recipient of a prescription request the user sends explicitly
    // from PrescriptionRequestDialog. Never used by the automated
    // notifications. Empty (the default, and the value for files
    // written before the field existed) means no doctor configured.
    public string DoctorAddress { get; set; } = string.Empty;

    // Caregiver options (docs/notes/EVOLUTION-PROPOSALS-2.md §3.8),
    // replicated like the addresses. CaregiverEmails: the kinds of
    // email copied to the caregiver (Notifications.CaregiverEmails;
    // "" = every kind). CaregiverDigest: "Weekly" sends the stock
    // summary. CaregiverDigestSentOn: day of the last summary sent by
    // any device of the profile (yyyy-MM-dd).
    public string CaregiverEmails { get; set; } = string.Empty;
    public string CaregiverDigest { get; set; } = string.Empty;
    public string CaregiverDigestSentOn { get; set; } = string.Empty;
}
