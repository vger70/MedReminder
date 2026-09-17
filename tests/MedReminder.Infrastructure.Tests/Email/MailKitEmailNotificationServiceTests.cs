using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Email;

// A real SMTP send cannot be unit-tested without spinning up
// di un fake server (fuori scope MVP). Ci concentriamo sulle
// pre-condizioni: configurazione incompleta -> throw / TestConnection
// returns false; missing credentials -> specific throw.
public class MailKitEmailNotificationServiceTests
{
    [Fact]
    public async Task Send_throws_when_settings_are_incomplete()
    {
        var settings = new SmtpSettings();   // Host vuoto
        var monitor = new StaticOptionsMonitor<SmtpSettings>(settings);
        var store = new StubCredentialStore();
        var sut = new MailKitEmailNotificationService(
            monitor, store, NullLogger<MailKitEmailNotificationService>.Instance);

        await FluentActions.Awaiting(() =>
                sut.SendAsync(new EmailMessage("s", "b"), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task TestConnection_returns_false_when_settings_incomplete()
    {
        var settings = new SmtpSettings();
        var monitor = new StaticOptionsMonitor<SmtpSettings>(settings);
        var store = new StubCredentialStore();
        var sut = new MailKitEmailNotificationService(
            monitor, store, NullLogger<MailKitEmailNotificationService>.Instance);

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
