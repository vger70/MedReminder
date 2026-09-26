using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Email;

// A real SMTP send cannot be unit-tested without spinning up a fake
// server (out of scope for the MVP). We focus on pre-conditions:
// incomplete configuration → throw / TestConnection returns false;
// missing per-profile recipient → specific throw.
public class MailKitEmailNotificationServiceTests
{
    [Fact]
    public async Task Send_throws_when_smtp_settings_are_incomplete()
    {
        var smtp = new StaticOptionsMonitor<SmtpSettings>(new SmtpSettings()); // Host empty
        var notifications = new StaticOptionsMonitor<NotificationSettings>(new NotificationSettings
        {
            ToAddress = "user@example.org",
        });
        var store = new StubCredentialStore();
        var sut = new MailKitEmailNotificationService(
            smtp, notifications, store, NullLogger<MailKitEmailNotificationService>.Instance);

        await FluentActions.Awaiting(() =>
                sut.SendAsync(new EmailMessage("s", "b"), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Send_throws_when_recipient_is_missing()
    {
        var smtp = new StaticOptionsMonitor<SmtpSettings>(new SmtpSettings
        {
            Host = "smtp.example.org",
            Port = 587,
            FromAddress = "sender@example.org",
        });
        var notifications = new StaticOptionsMonitor<NotificationSettings>(new NotificationSettings());
        var store = new StubCredentialStore();
        var sut = new MailKitEmailNotificationService(
            smtp, notifications, store, NullLogger<MailKitEmailNotificationService>.Instance);

        await FluentActions.Awaiting(() =>
                sut.SendAsync(new EmailMessage("s", "b"), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*recipient*");
    }

    // A3 (docs/analysis/ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md §8.2):
    // the caregiver fan-out lives in BuildMimeMessage. These tests
    // assert on the recipient list directly, without a real SMTP send.
    private static SmtpSettings ValidSmtp() => new()
    {
        Host = "smtp.example.org",
        Port = 587,
        FromAddress = "sender@example.org",
        FromDisplayName = "MedReminder",
    };

    private static MailKitEmailNotificationService BuildService(NotificationSettings notifications)
    {
        var smtp = new StaticOptionsMonitor<SmtpSettings>(ValidSmtp());
        var monitor = new StaticOptionsMonitor<NotificationSettings>(notifications);
        var store = new StubCredentialStore();
        return new MailKitEmailNotificationService(
            smtp, monitor, store, NullLogger<MailKitEmailNotificationService>.Instance);
    }

    [Fact]
    public void BuildMimeMessage_uses_single_recipient_when_caregiver_empty()
    {
        var notifications = new NotificationSettings { ToAddress = "user@example.org" };
        var sut = BuildService(notifications);

        var mime = sut.BuildMimeMessage(ValidSmtp(), notifications, new EmailMessage("s", "b"));

        var recipients = mime.To.Mailboxes.Select(m => m.Address).ToArray();
        recipients.Should().ContainSingle().Which.Should().Be("user@example.org");
    }

    [Fact]
    public void BuildMimeMessage_adds_caregiver_as_second_recipient_primary_first()
    {
        var notifications = new NotificationSettings
        {
            ToAddress = "user@example.org",
            CaregiverAddress = "caregiver@example.org",
        };
        var sut = BuildService(notifications);

        var mime = sut.BuildMimeMessage(ValidSmtp(), notifications, new EmailMessage("s", "b"));

        var recipients = mime.To.Mailboxes.Select(m => m.Address).ToArray();
        recipients.Should().Equal("user@example.org", "caregiver@example.org");
    }

    [Fact]
    public void BuildMimeMessage_falls_back_to_primary_when_caregiver_malformed()
    {
        var notifications = new NotificationSettings
        {
            ToAddress = "user@example.org",
            CaregiverAddress = "not-an-email",
        };
        var sut = BuildService(notifications);

        // Must not throw — a bad caregiver value hand-edited into the
        // JSON file becomes a warning + primary-only send (§4.3).
        var mime = sut.BuildMimeMessage(ValidSmtp(), notifications, new EmailMessage("s", "b"));

        var recipients = mime.To.Mailboxes.Select(m => m.Address).ToArray();
        recipients.Should().ContainSingle().Which.Should().Be("user@example.org");
    }

    [Fact]
    public void BuildMimeMessage_deduplicates_caregiver_equal_to_primary()
    {
        // Hand-edited JSON where caregiver == primary (differing only in
        // case / whitespace) must not produce a two-recipient message
        // with the same address twice (§8.2).
        var notifications = new NotificationSettings
        {
            ToAddress = "user@example.org",
            CaregiverAddress = "  USER@example.org  ",
        };
        var sut = BuildService(notifications);

        var mime = sut.BuildMimeMessage(ValidSmtp(), notifications, new EmailMessage("s", "b"));

        var recipients = mime.To.Mailboxes.Select(m => m.Address).ToArray();
        recipients.Should().ContainSingle().Which.Should().Be("user@example.org");
    }

    // Prescription request: an explicit recipient replaces the profile
    // recipients entirely; the caregiver is never copied.
    [Fact]
    public void BuildMimeMessage_sends_only_to_the_explicit_recipient()
    {
        var notifications = new NotificationSettings
        {
            ToAddress = "user@example.org",
            CaregiverAddress = "caregiver@example.org",
            DoctorAddress = "doctor@example.org",
        };
        var sut = BuildService(notifications);

        var mime = sut.BuildMimeMessage(ValidSmtp(), notifications,
            new EmailMessage("s", "b", " doctor@example.org "));

        mime.To.Mailboxes.Select(m => m.Address).Should().Equal("doctor@example.org");
        mime.Cc.Should().BeEmpty();
        mime.Bcc.Should().BeEmpty();
        mime.Subject.Should().Be("s");
    }

    [Fact]
    public void BuildMimeMessage_rejects_a_malformed_explicit_recipient()
    {
        var notifications = new NotificationSettings { ToAddress = "user@example.org" };
        var sut = BuildService(notifications);

        // No fallback to the profile recipient: the user chose the
        // address and must see the failure.
        FluentActions.Invoking(() => sut.BuildMimeMessage(ValidSmtp(), notifications,
                new EmailMessage("s", "b", "not-an-email")))
            .Should().Throw<MimeKit.ParseException>();
    }

    [Fact]
    public async Task Send_with_explicit_recipient_does_not_require_the_profile_recipient()
    {
        // Incomplete SMTP makes the send fail before any network I/O;
        // the point is that the failure is the SMTP one, not the
        // "recipient is not configured" guard.
        var smtp = new StaticOptionsMonitor<SmtpSettings>(new SmtpSettings());
        var notifications = new StaticOptionsMonitor<NotificationSettings>(new NotificationSettings());
        var sut = new MailKitEmailNotificationService(
            smtp, notifications, new StubCredentialStore(),
            NullLogger<MailKitEmailNotificationService>.Instance);

        await FluentActions.Awaiting(() =>
                sut.SendAsync(new EmailMessage("s", "b", "doctor@example.org"), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("SMTP configuration is incomplete.");
    }

    [Fact]
    public async Task TestConnection_returns_false_when_settings_incomplete()
    {
        var smtp = new StaticOptionsMonitor<SmtpSettings>(new SmtpSettings());
        var notifications = new StaticOptionsMonitor<NotificationSettings>(new NotificationSettings());
        var store = new StubCredentialStore();
        var sut = new MailKitEmailNotificationService(
            smtp, notifications, store, NullLogger<MailKitEmailNotificationService>.Instance);

        var ok = await sut.TestConnectionAsync(CancellationToken.None);
        ok.Should().BeFalse();
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;
        public StaticOptionsMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class StubCredentialStore : ISmtpCredentialStore
    {
        public bool HasPassword => false;
        public string? GetPassword() => null;
        public void SetPassword(string password) { }
        public void Clear() { }
    }
}
