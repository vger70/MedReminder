using MedReminder.Application.Abstractions;
using MedReminder.Application.Calendar;
using MedReminder.Application.Notifications;
using MedReminder.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedReminder.MobileSpikes.Spikes;

// What the user types for S10. Never written to the report or a log:
// the report holds the host, the port and the outcome only.
internal sealed record SmtpInput(string Host, int Port, bool StartTls, string Username, string Password, string Recipient);

// S10 — MailKit on Android (ANALYSIS-B1-ANDROID-PLAN.md §4.5): the
// production MailKitEmailNotificationService, compiled from the desktop
// sources, builds a message with a calendar attachment, connects with
// TLS through the phone's trust store, authenticates and sends. Run in
// the Release APK so trimming and AOT apply.
internal static class S10MailKit
{
    private const string Spike = "S10";

    public static void BuildMessage(SpikeReport report)
        => SpikeRunner.Check(report, Spike, "Build a message with an .ics attachment (offline)", () =>
        {
            var service = Create(new SmtpSettings { Host = "smtp.invalid", FromAddress = "from@example.org" }, string.Empty);
            var message = new EmailMessage(
                "MedReminder S10",
                "Synthetic body.",
                ExplicitRecipient: "to@example.org",
                CalendarEvent: new CalendarEvent("s10@medreminder", DateOnly.FromDateTime(DateTime.Today), "S10 event"));
            var mime = service.BuildMimeMessage(new SmtpSettings { Host = "smtp.invalid", FromAddress = "from@example.org" }, new NotificationSettings(), message);
            using var buffer = new MemoryStream();
            mime.WriteTo(buffer);
            var text = System.Text.Encoding.ASCII.GetString(buffer.ToArray());
            var ok = text.Contains("text/calendar", StringComparison.Ordinal)
                && text.Contains("medreminder.ics", StringComparison.Ordinal);
            return (ok, ok ? $"{buffer.Length} bytes, multipart with text/calendar" : "attachment missing");
        });

    public static async Task RunAsync(SpikeReport report, SmtpInput input, bool send, CancellationToken cancellationToken)
    {
        var settings = new SmtpSettings
        {
            Host = input.Host.Trim(),
            Port = input.Port,
            UseStartTls = input.StartTls,
            Username = input.Username.Trim(),
            FromAddress = input.Username.Contains('@', StringComparison.Ordinal) ? input.Username.Trim() : input.Recipient.Trim(),
            TimeoutSeconds = 30,
        };
        var service = Create(settings, input.Password);
        var endpoint = $"{settings.Host}:{settings.Port}, {(settings.UseStartTls ? "STARTTLS" : "auto TLS")}";

        await SpikeRunner.CheckAsync(report, Spike, "Connect, TLS and authenticate (TestConnectionAsync)", async () =>
        {
            var ok = await service.TestConnectionAsync(cancellationToken);
            return (ok, ok ? endpoint : $"{endpoint}: failed (see the SMTP provider's app-password rules)");
        });

        if (!send)
        {
            return;
        }

        await SpikeRunner.CheckAsync(report, Spike, "Send a test message with an .ics attachment (SendAsync)", async () =>
        {
            await service.SendAsync(new EmailMessage(
                "MedReminder S10 test",
                "Test message sent by the MedReminder Android spike S10. No health data.",
                ExplicitRecipient: input.Recipient.Trim(),
                CalendarEvent: new CalendarEvent("s10@medreminder", DateOnly.FromDateTime(DateTime.Today), "MedReminder S10 test")),
                cancellationToken);
            return (true, $"{endpoint}: accepted by the server; check the inbox");
        });
    }

    private static MailKitEmailNotificationService Create(SmtpSettings settings, string password)
        => new(
            new FixedMonitor<SmtpSettings>(settings),
            new FixedMonitor<NotificationSettings>(new NotificationSettings()),
            new MemoryCredentialStore(password),
            NullLogger<MailKitEmailNotificationService>.Instance);

    private sealed class FixedMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class MemoryCredentialStore(string password) : ISmtpCredentialStore
    {
        private string _password = password;

        public bool HasPassword => _password.Length > 0;

        public string? GetPassword() => _password;

        public void SetPassword(string password) => _password = password;

        public void Clear() => _password = string.Empty;
    }
}
