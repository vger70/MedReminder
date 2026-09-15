using System.Net.Sockets;
using MailKit.Net.Smtp;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Email;

// Decoratore che aggiunge retry con back-off su errori transitivi
// dell'invio SMTP. Ragione (spec §19): un errore email non deve fermare
// l'app né duplicare le notifiche; ma un failure transiente (rete che
// oscilla, quota momentanea 4xx) va ritentato prima di rassegnarsi.
//
// Back-off fissi: 5s → 30s → 2m (tre tentativi oltre il primo). Nessun
// jitter randomico — è un'app desktop single-user, non un client web
// concorrente. Errori 5xx (autenticazione, destinatario non valido) NON
// vengono ritentati: sono errori di configurazione, non transitivi.
internal sealed class RetryingEmailNotificationService : IEmailNotificationService
{
    private static readonly TimeSpan[] Backoffs =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
    };

    private readonly IEmailNotificationService _inner;
    private readonly TimeProvider _clock;
    private readonly ILogger<RetryingEmailNotificationService> _log;

    public RetryingEmailNotificationService(
        IEmailNotificationService inner,
        TimeProvider clock,
        ILogger<RetryingEmailNotificationService> log)
    {
        _inner = inner;
        _clock = clock;
        _log = log;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt <= Backoffs.Length; attempt++)
        {
            try
            {
                await _inner.SendAsync(message, cancellationToken);
                if (attempt > 0)
                {
                    _log.LogInformation("SMTP send succeeded on retry {Attempt}", attempt);
                }
                return;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastError = ex;
                if (attempt == Backoffs.Length)
                {
                    _log.LogWarning(ex,
                        "SMTP send exhausted retries (attempt {Attempt}); giving up.", attempt);
                    break;
                }
                var delay = Backoffs[attempt];
                _log.LogWarning(ex,
                    "SMTP transient error (attempt {Attempt}); retrying in {DelaySeconds}s.",
                    attempt, delay.TotalSeconds);
                await Task.Delay(delay, _clock, cancellationToken);
            }
            // Le eccezioni non transitivie escono direttamente: non ha
            // senso ritentare un 5xx autenticazione o destinatario invalido.
        }

        throw lastError!;
    }

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
        => _inner.TestConnectionAsync(cancellationToken);

    private static bool IsTransient(Exception ex) => ex switch
    {
        SmtpCommandException scx => IsTransientSmtpStatusCode(scx.StatusCode),
        SmtpProtocolException => true,
        SocketException => true,
        TimeoutException => true,
        IOException => true,
        _ => false,
    };

    // Codici SMTP 4xx sono errori temporanei ("try again later"); 5xx sono
    // errori permanenti (autenticazione, destinatario inesistente, ecc.).
    private static bool IsTransientSmtpStatusCode(MailKit.Net.Smtp.SmtpStatusCode code)
    {
        var value = (int)code;
        return value is >= 400 and < 500;
    }
}
