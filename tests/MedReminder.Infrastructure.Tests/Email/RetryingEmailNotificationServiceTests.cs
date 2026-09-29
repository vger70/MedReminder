using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Email;

// Interactive explicit-recipient sends (prescription request) bypass
// the back-off: the user is waiting on the dialog, and the retry log
// entries attach the exception, whose text can echo the recipient.
public class RetryingEmailNotificationServiceTests
{
    [Fact]
    public async Task Explicit_recipient_send_is_not_retried_on_a_transient_error()
    {
        var inner = new TransientFailingEmailService();
        var sut = new RetryingEmailNotificationService(
            inner, TimeProvider.System, NullLogger<RetryingEmailNotificationService>.Instance);

        await FluentActions.Awaiting(() =>
                sut.SendAsync(new EmailMessage("s", "b", "doctor@example.org"), CancellationToken.None))
            .Should().ThrowAsync<IOException>();

        inner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Explicit_recipient_send_is_forwarded_unchanged()
    {
        var inner = new RecordingEmailService();
        var sut = new RetryingEmailNotificationService(
            inner, TimeProvider.System, NullLogger<RetryingEmailNotificationService>.Instance);
        var message = new EmailMessage("s", "b", "doctor@example.org");

        await sut.SendAsync(message, CancellationToken.None);

        inner.Sent.Should().ContainSingle().Which.Should().Be(message);
    }

    private sealed class TransientFailingEmailService : IEmailNotificationService
    {
        public int Calls { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Calls++;
            throw new IOException("connection reset");
        }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class RecordingEmailService : IEmailNotificationService
    {
        public List<EmailMessage> Sent { get; } = new();

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
