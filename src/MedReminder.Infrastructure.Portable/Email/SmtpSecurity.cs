using MailKit.Net.Smtp;
using MailKit.Security;

namespace MedReminder.Infrastructure.Email;

// The encryption rule of every SMTP connection, the sender's and the
// connection test's (B5-01 follow-up):
//   - the password is never sent on a connection without TLS: with a user
//     name the connection must be encrypted before AUTH;
//   - "Use STARTTLS" means encryption is required, not preferred: a server
//     that does not offer STARTTLS (or a man in the middle that strips it)
//     is refused instead of continuing in clear;
//   - only without a user name and with STARTTLS off may a connection stay
//     plain (a local relay that needs no sign-in);
//   - port 465 is encrypted from the start (implicit TLS) whatever the
//     STARTTLS setting, since STARTTLS cannot work there.
//   - the server certificate is validated (trusted chain, host name, dates)
//     but its revocation is not checked: MailKit's default fails the TLS
//     handshake whenever the OCSP or CRL answer cannot be fetched
//     ("an incomplete certificate revocation check occurred"), which on
//     phones and some networks refused smtp.gmail.com on 587 and 465.
// The check runs after ConnectAsync, when MailKit has done the implicit
// TLS or the STARTTLS upgrade, and before anything else is sent.
internal static class SmtpSecurity
{
    public const int ImplicitTlsPort = 465;

    // Every SMTP client of the sender and of the connection test.
    internal static SmtpClient NewClient(int timeoutMilliseconds)
        => new() { Timeout = timeoutMilliseconds, CheckCertificateRevocation = false };

    // Implicit TLS on 465; otherwise STARTTLS when the server offers it, or
    // what MailKit's Auto implies. RequireEncrypted then refuses a plain
    // result where encryption is required.
    internal static SecureSocketOptions SecureOption(bool useStartTls, int port)
        => port == ImplicitTlsPort ? SecureSocketOptions.SslOnConnect
            : useStartTls ? SecureSocketOptions.StartTlsWhenAvailable
            : SecureSocketOptions.Auto;

    internal static bool RequiresEncryption(bool useStartTls, string? username)
        => useStartTls || !string.IsNullOrWhiteSpace(username);

    internal static void RequireEncrypted(SmtpClient client, bool useStartTls, string? username)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (RequiresEncryption(useStartTls, username) && !client.IsSecure)
        {
            throw new SmtpEncryptionRequiredException();
        }
    }
}

// The server did not give an encrypted connection where one is required
// (see SmtpSecurity). Not transient: the retry decorator does not retry
// it. The message names no server, user or address.
public sealed class SmtpEncryptionRequiredException : InvalidOperationException
{
    public SmtpEncryptionRequiredException()
        : base("The SMTP server did not provide an encrypted connection; the credentials and the message are not sent without encryption.")
    {
    }
}
