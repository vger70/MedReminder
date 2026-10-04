using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Calendar;
using MedReminder.Application.Notifications;
using MedReminder.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
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
        mime.Cc.Mailboxes.Should().BeEmpty();
        mime.Bcc.Mailboxes.Should().BeEmpty();
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

    // EVOLUTION-PROPOSALS-2 §3.7: a calendar event travels as an .ics
    // attachment next to the text; a message without one stays plain.
    [Fact]
    public void BuildMimeMessage_attaches_the_calendar_event()
    {
        var notifications = new NotificationSettings { ToAddress = "user@example.org" };
        var sut = BuildService(notifications);
        var calendarEvent = new CalendarEvent("runout-1@medreminder", new DateOnly(2026, 10, 20), "MedReminder: a medicine runs out");

        var mime = sut.BuildMimeMessage(ValidSmtp(), notifications,
            new EmailMessage("s", "b", CalendarEvent: calendarEvent));

        mime.TextBody.Should().Be("b");
        var attachment = mime.Attachments.OfType<MimePart>().Should().ContainSingle().Subject;
        attachment.FileName.Should().Be("medreminder.ics");
        attachment.ContentType.MimeType.Should().Be("text/calendar");
        attachment.ContentType.Parameters["method"].Should().Be("PUBLISH");
        using var content = new MemoryStream();
        attachment.Content!.DecodeTo(content);
        var ics = System.Text.Encoding.UTF8.GetString(content.ToArray());
        ics.Should().Contain("DTSTART;VALUE=DATE:20261020\r\n")
            .And.Contain("UID:runout-1@medreminder");

        sut.BuildMimeMessage(ValidSmtp(), notifications, new EmailMessage("s", "b"))
            .Body.Should().BeOfType<TextPart>();
    }

    // EVOLUTION-PROPOSALS-2 §3.8: the caregiver gets only the kinds the
    // profile chose; every kind while the setting is empty.
    [Theory]
    [InlineData("", EmailKind.DoseReminder, true)]
    [InlineData("LowStock", EmailKind.LowStock, true)]
    [InlineData("LowStock", EmailKind.DoseReminder, false)]
    [InlineData("None", EmailKind.LowStock, false)]
    public void BuildMimeMessage_copies_the_caregiver_only_for_the_chosen_kinds(
        string chosen, EmailKind kind, bool copied)
    {
        var notifications = new NotificationSettings
        {
            ToAddress = "user@example.org",
            CaregiverAddress = "caregiver@example.org",
            CaregiverEmails = chosen,
        };
        var sut = BuildService(notifications);

        var mime = sut.BuildMimeMessage(ValidSmtp(), notifications, new EmailMessage("s", "b", Kind: kind));

        mime.To.Mailboxes.Select(m => m.Address).Should().Equal(
            copied ? ["user@example.org", "caregiver@example.org"] : ["user@example.org"]);
    }

    [Fact]
    public void BuildMimeMessage_sends_the_digest_to_the_caregiver_only()
    {
        var notifications = new NotificationSettings
        {
            ToAddress = "user@example.org",
            CaregiverAddress = "caregiver@example.org",
            CaregiverEmails = "None",
        };
        var sut = BuildService(notifications);

        var mime = sut.BuildMimeMessage(ValidSmtp(), notifications, new EmailMessage("s", "b", Kind: EmailKind.Digest));

        mime.To.Mailboxes.Select(m => m.Address).Should().Equal("caregiver@example.org");
    }

    [Fact]
    public async Task SendAsync_refuses_a_digest_without_caregiver()
    {
        var sut = BuildService(new NotificationSettings { ToAddress = "user@example.org" });

        var act = () => sut.SendAsync(new EmailMessage("s", "b", Kind: EmailKind.Digest), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
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
