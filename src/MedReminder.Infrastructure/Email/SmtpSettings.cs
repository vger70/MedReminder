namespace MedReminder.Infrastructure.Email;

// SMTP transport configuration (global, admin-managed in the
// multi-profile setup — docs/ANALYSIS-MULTI-USER.md §7.1). The
// password is NOT here: it lives in the DPAPI-encrypted file
// managed by ISmtpCredentialStore.
//
// Increment 15c: ToAddress moved to
// MedReminder.Application.Abstractions.NotificationSettings — that
// is the only piece of the email configuration a non-admin user
// personalises. IsConfigured no longer checks it: an SMTP server
// without any active profile recipient is technically valid, and
// MailKitEmailNotificationService gates the recipient separately.
public sealed class SmtpSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = "MedReminder";
    public int TimeoutSeconds { get; set; } = 30;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && Port > 0
        && !string.IsNullOrWhiteSpace(FromAddress);
}
