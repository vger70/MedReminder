using System.Net.Sockets;
using MailKit.Net.Smtp;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Email;

// Decorator that adds retry with back-off on transient SMTP-send
// errors. Rationale (spec §19): an email error must neither stop the
// app nor duplicate notifications; but a transient failure (jittery
// network, momentary 4xx quota) should be retried before giving up.
//
// Fixed back-offs: 5s → 30s → 2m (three retries beyond the first).
// No random jitter — this is a single-user desktop app, not a
// concurrent web client. 5xx errors (authentication, invalid
// recipient) are NOT retried: they are configuration errors, not
// transient.
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
        ArgumentNullException.ThrowIfNull(message);

        // Interactive explicit-recipient sends (prescription request)
        // are not retried: the user is waiting on the dialog and can
        // retry by hand, and the back-off log entries below attach the
        // exception, whose SMTP text can echo the recipient address.
        if (message.ExplicitRecipient is not null)
        {
            await _inner.SendAsync(message, cancellationToken);
            return;
        }

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
            // Non-transient exceptions bubble up directly: there is
            // no point retrying a 5xx authentication or invalid
            // recipient.
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

    // SMTP 4xx codes are temporary errors ("try again later"); 5xx
    // codes are permanent (authentication, non-existent recipient,
    // etc.).
    private static bool IsTransientSmtpStatusCode(MailKit.Net.Smtp.SmtpStatusCode code)
    {
        var value = (int)code;
        return value is >= 400 and < 500;
    }
}
