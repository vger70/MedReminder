using MailKit.Net.Smtp;
using MailKit.Security;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MedReminder.Infrastructure.Email;

// SMTP tramite MailKit (System.Net.Mail.SmtpClient è marcato obsoleto
// da .NET 6+, docs/ANALYSIS.md §1.1 punto 10). Adapter thin: apre una
// connessione per invio, autentica se username/password sono presenti,
// invia il messaggio, chiude. Nessun retry qui: il retry con back-off
// vive nel monitor (Incremento 7 hardening).
internal sealed class MailKitEmailNotificationService : IEmailNotificationService
{
    private readonly IOptionsMonitor<SmtpSettings> _settingsMonitor;
    private readonly ISmtpCredentialStore _credentialStore;
    private readonly ILogger<MailKitEmailNotificationService> _log;

    public MailKitEmailNotificationService(
        IOptionsMonitor<SmtpSettings> settingsMonitor,
        ISmtpCredentialStore credentialStore,
        ILogger<MailKitEmailNotificationService> log)
    {
        _settingsMonitor = settingsMonitor;
        _credentialStore = credentialStore;
        _log = log;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = _settingsMonitor.CurrentValue;
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("Configurazione SMTP incompleta.");
        }

        var mime = BuildMimeMessage(settings, message);
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
            settings.ToAddress, settings.Host, settings.Port);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsMonitor.CurrentValue;
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
                "Credenziali SMTP non presenti nel credential store cifrato.");
        }
        await client.AuthenticateAsync(settings.Username, password, cancellationToken);
    }

    private static MimeMessage BuildMimeMessage(SmtpSettings settings, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromDisplayName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(settings.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };
        return mime;
    }
}
