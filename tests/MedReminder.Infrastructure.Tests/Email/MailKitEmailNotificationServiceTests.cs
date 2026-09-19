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
