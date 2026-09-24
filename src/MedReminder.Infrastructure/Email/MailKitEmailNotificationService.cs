using MailKit.Net.Smtp;
using MailKit.Security;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MedReminder.Infrastructure.Email;

// SMTP via MailKit (System.Net.Mail.SmtpClient has been marked
// obsolete since .NET 6+, docs/ANALYSIS.md §1.1 item 10). Thin
// adapter: opens a connection per send, authenticates if
// username / password are present, sends the message, disconnects.
// No retry here: the retry with back-off lives in the monitor
// (Increment 7 hardening).
//
// Increment 15c (docs/ANALYSIS-MULTI-USER.md §7.1): the recipient
// is per-profile — it lives on NotificationSettings loaded from
// <DataDirectory>\notifications.settings.json — while everything
// else stays on SmtpSettings, loaded from the global
// smtp.settings.json under %LOCALAPPDATA%\MedReminder\.
internal sealed class MailKitEmailNotificationService : IEmailNotificationService
{
    private readonly IOptionsMonitor<SmtpSettings> _smtpMonitor;
    private readonly IOptionsMonitor<NotificationSettings> _notificationMonitor;
    private readonly ISmtpCredentialStore _credentialStore;
    private readonly ILogger<MailKitEmailNotificationService> _log;

    // MimeKit accepts a bare local part (e.g. "not-an-email") as a
    // valid mailbox by default (ParserOptions.AllowAddressesWithoutDomain
    // is true). A caregiver address must be a full mailbox with a
    // domain, so parse with that leniency turned off: a value without a
    // domain is treated as malformed and falls back to primary-only
    // (A3 §4.3). The save-time UI validation in SettingsDialog mirrors
    // this rule with its own ParserOptions so the dialog accepts exactly
    // what the adapter accepts.
    internal static readonly ParserOptions AddressParserOptions = new()
    {
        AllowAddressesWithoutDomain = false,
    };

    public MailKitEmailNotificationService(
        IOptionsMonitor<SmtpSettings> smtpMonitor,
        IOptionsMonitor<NotificationSettings> notificationMonitor,
        ISmtpCredentialStore credentialStore,
        ILogger<MailKitEmailNotificationService> log)
    {
        _smtpMonitor = smtpMonitor;
        _notificationMonitor = notificationMonitor;
        _credentialStore = credentialStore;
        _log = log;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = _smtpMonitor.CurrentValue;
        var notifications = _notificationMonitor.CurrentValue;
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("SMTP configuration is incomplete.");
        }
        if (string.IsNullOrWhiteSpace(notifications.ToAddress))
        {
            throw new InvalidOperationException(
                "Notification recipient is not configured for the current profile.");
        }

        var mime = BuildMimeMessage(settings, notifications, message);
        using var client = new SmtpClient
        {
            Timeout = settings.TimeoutSeconds * 1000,
        };

        var secureOption = settings.UseStartTls
            ? SecureSocketOptions.StartTlsWhenAvailable
            : SecureSocketOptions.Auto;

        await client.ConnectAsync(settings.Host, settings.Port, secureOption, cancellationToken);
        await AuthenticateIfNeededAsync(client, settings, cancellationToken);
        try
        {
            await client.SendAsync(mime, cancellationToken);
        }
        finally
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }

        _log.LogInformation(
            "Email notification sent to {ToAddress} via {Host}:{Port}",
            notifications.ToAddress, settings.Host, settings.Port);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
    {
        var settings = _smtpMonitor.CurrentValue;
        if (!settings.IsConfigured) return false;
        try
        {
            using var client = new SmtpClient
            {
                Timeout = settings.TimeoutSeconds * 1000,
            };
            var secureOption = settings.UseStartTls
                ? SecureSocketOptions.StartTlsWhenAvailable
                : SecureSocketOptions.Auto;
            await client.ConnectAsync(settings.Host, settings.Port, secureOption, cancellationToken);
            await AuthenticateIfNeededAsync(client, settings, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SMTP connection test failed for {Host}:{Port}", settings.Host, settings.Port);
            return false;
        }
    }

    private async Task AuthenticateIfNeededAsync(
        SmtpClient client, SmtpSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.Username)) return;
        var password = _credentialStore.GetPassword();
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "SMTP credentials not present in the encrypted credential store.");
        }
        await client.AuthenticateAsync(settings.Username, password, cancellationToken);
    }

    // Internal (not private) so the infrastructure tests can assert on
    // the recipient list without spinning up a real SMTP server
    // (see MailKitEmailNotificationServiceTests). The instance
    // dependency is only the logger, needed for the caregiver
    // malformed-address warning (A3 §4.3).
    internal MimeMessage BuildMimeMessage(
        SmtpSettings settings, NotificationSettings notifications, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromDisplayName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(notifications.ToAddress));

        // A3 (docs/analysis/ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md §4.2,
        // §6): when a caregiver address is configured, add it as a
        // second To recipient in the same message. A malformed value
        // must not block delivery to the primary (§4.3), so the parse
        // is defensive: on failure we log a warning and fall back to
        // primary-only. A self-copy (caregiver == primary) is skipped
        // so the message is not built with the same address twice —
        // save-time UI validation is the primary defence; this guards
        // hand-edited JSON.
        if (!string.IsNullOrWhiteSpace(notifications.CaregiverAddress))
        {
            var caregiver = notifications.CaregiverAddress.Trim();
            if (!string.Equals(caregiver, notifications.ToAddress.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    mime.To.Add(MailboxAddress.Parse(AddressParserOptions, caregiver));
                }
                catch (ParseException ex)
                {
                    _log.LogWarning(ex,
                        "Caregiver address is malformed; sending to primary only.");
                }
            }
        }

        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };
        return mime;
    }
}
