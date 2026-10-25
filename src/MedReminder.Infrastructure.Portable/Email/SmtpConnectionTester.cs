using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Email;

// The result of an SMTP connection test, in categories a form can turn
// into recovery guidance (Android backlog B5-01). Nothing is sent.
public enum SmtpTestOutcome
{
    // The server accepted the connection and, with a user name, the
    // credentials.
    Accepted,
    // The connection stayed plain where encryption is required (STARTTLS
    // on, or a user name set): nothing more was sent, the password
    // included (SmtpSecurity).
    EncryptionUnavailable,
    // Host or sender address missing: nothing was tried.
    NotConfigured,
    // A user name without a password: nothing was tried.
    PasswordMissing,
    // The server name does not resolve.
    HostNotFound,
    // The server could not be reached on that port (refused, unreachable,
    // connection dropped).
    ConnectionFailed,
    // The server did not answer in time.
    TimedOut,
    // The encrypted connection could not be set up (certificate not
    // trusted, TLS on the wrong port, protocol mismatch).
    SecureConnectionFailed,
    // The server refused the user name or the password.
    AuthenticationFailed,
    // A user name is set but the server offers no sign-in.
    AuthenticationNotOffered,
    // The server answered with an error to a command.
    Rejected,
}

// Encrypted: the connection was protected by TLS (implicit or STARTTLS)
// when the test ended; false on a plain connection or when the test
// stopped earlier. Accepted with Encrypted false happens only without a
// user name and with STARTTLS off. Error is the cause, for the host's log only: its text
// can carry server replies and is never shown as is.
public sealed record SmtpTestResult(SmtpTestOutcome Outcome, bool Encrypted)
{
    internal Exception? Error { get; init; }
}

// Connects, signs in when a user name is set, and disconnects, with the
// security option and the encryption rule the MailKit sender uses
// (SmtpSecurity). Works on a configuration not
// saved yet: the form passes what is typed, and the password typed or
// the stored one.
public sealed class SmtpConnectionTester
{
    // Connect, greeting, EHLO, STARTTLS and AUTH each get the configured
    // timeout from MailKit; this outer bound only covers a stalled step
    // that MailKit's own timeout does not catch.
    private const int OuterBoundFactor = 3;

    // Tests only: accepts the loopback server's self-signed certificate.
    // Null keeps MailKit's certificate validation.
    internal System.Net.Security.RemoteCertificateValidationCallback? ServerCertificateValidation { get; init; }

    public async Task<SmtpTestResult> TestAsync(SmtpTransport transport, string? password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (string.IsNullOrWhiteSpace(transport.Host) || transport.Port <= 0
            || string.IsNullOrWhiteSpace(transport.FromAddress))
        {
            return new SmtpTestResult(SmtpTestOutcome.NotConfigured, false);
        }
        var username = transport.Username?.Trim() ?? string.Empty;
        if (username.Length > 0 && string.IsNullOrEmpty(password))
        {
            return new SmtpTestResult(SmtpTestOutcome.PasswordMissing, false);
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(1, transport.TimeoutSeconds));
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(timeout * OuterBoundFactor);
        using var client = SmtpSecurity.NewClient((int)timeout.TotalMilliseconds);
        if (ServerCertificateValidation is { } validation) client.ServerCertificateValidationCallback = validation;
        try
        {
            await client.ConnectAsync(transport.Host.Trim(), transport.Port,
                SmtpSecurity.SecureOption(transport.UseStartTls, transport.Port), bound.Token);
            SmtpSecurity.RequireEncrypted(client, transport.UseStartTls, username);
            if (username.Length > 0)
            {
                await client.AuthenticateAsync(username, password!, bound.Token);
            }
            var encrypted = client.IsSecure;
            await client.DisconnectAsync(quit: true, bound.Token);
            return new SmtpTestResult(SmtpTestOutcome.Accepted, encrypted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SmtpTestResult(Classify(ex), client.IsConnected && client.IsSecure) { Error = ex };
        }
    }

    internal static SmtpTestOutcome Classify(Exception exception) => exception switch
    {
        SmtpEncryptionRequiredException => SmtpTestOutcome.EncryptionUnavailable,
        AuthenticationException => SmtpTestOutcome.AuthenticationFailed,
        SslHandshakeException => SmtpTestOutcome.SecureConnectionFailed,
        System.Security.Authentication.AuthenticationException => SmtpTestOutcome.SecureConnectionFailed,
        SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain }
            => SmtpTestOutcome.HostNotFound,
        SocketException { SocketErrorCode: SocketError.TimedOut } => SmtpTestOutcome.TimedOut,
        SocketException => SmtpTestOutcome.ConnectionFailed,
        TimeoutException => SmtpTestOutcome.TimedOut,
        // The outer bound fired (the caller did not cancel).
        OperationCanceledException => SmtpTestOutcome.TimedOut,
        // MailKit: "The SMTP server does not support authentication."
        NotSupportedException => SmtpTestOutcome.AuthenticationNotOffered,
        SmtpCommandException => SmtpTestOutcome.Rejected,
        SmtpProtocolException => SmtpTestOutcome.Rejected,
        ServiceNotConnectedException => SmtpTestOutcome.ConnectionFailed,
        IOException { InnerException: SocketException inner } => Classify(inner),
        IOException => SmtpTestOutcome.ConnectionFailed,
        _ => SmtpTestOutcome.Rejected,
    };
}
