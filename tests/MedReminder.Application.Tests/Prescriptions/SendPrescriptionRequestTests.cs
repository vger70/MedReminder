using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Tests.Support;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MedReminder.Application.Tests.Prescriptions;

// The prescription request carries health data and personal names:
// the send path must log the outcome only, never the subject, the
// body or the recipient (CLAUDE.md §7), not even through an attached
// exception whose SMTP text echoes the recipient.
public class SendPrescriptionRequestTests
{
    private const string Recipient = "dr.rossi@example.org";
    private const string Subject = "Prescription request: Enalapril Teva";
    private const string Body = "Dear Dr. Rossi,\n\nMedicine: Enalapril Teva\n\nMario Bianchi";

    [Fact]
    public async Task Sends_the_draft_to_the_explicit_recipient_only()
    {
        var email = new RecordingEmailNotificationService();
        var sut = new SendPrescriptionRequest(email, new CapturingLogger<SendPrescriptionRequest>());

        await sut.ExecuteAsync("  " + Recipient + " ", Subject, Body, CancellationToken.None);

        email.Sent.Should().ContainSingle()
            .Which.Should().Be(new EmailMessage(Subject, Body, Recipient));
    }

    [Fact]
    public async Task Success_logs_the_outcome_without_sensitive_values()
    {
        var log = new CapturingLogger<SendPrescriptionRequest>();
        var sut = new SendPrescriptionRequest(new RecordingEmailNotificationService(), log);

        await sut.ExecuteAsync(Recipient, Subject, Body, CancellationToken.None);

        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information && e.Message.Contains("sent"));
        AssertNoSensitiveValues(log);
    }

    [Fact]
    public async Task Failure_logs_the_exception_type_only_and_rethrows()
    {
        var log = new CapturingLogger<SendPrescriptionRequest>();
        var email = new LeakyFailingEmailService();
        var sut = new SendPrescriptionRequest(email, log);

        await FluentActions.Awaiting(() =>
                sut.ExecuteAsync(Recipient, Subject, Body, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        var entry = log.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Exception.Should().BeNull("the exception text can echo the recipient");
        entry.Message.Should().Contain(nameof(InvalidOperationException));
        AssertNoSensitiveValues(log);
    }

    [Fact]
    public async Task Cancellation_is_logged_without_sensitive_values_and_rethrown()
    {
        var log = new CapturingLogger<SendPrescriptionRequest>();
        var sut = new SendPrescriptionRequest(new CancellingEmailService(), log);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await FluentActions.Awaiting(() =>
                sut.ExecuteAsync(Recipient, Subject, Body, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        AssertNoSensitiveValues(log);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_a_blank_recipient_without_sending(string recipient)
    {
        var email = new RecordingEmailNotificationService();
        var sut = new SendPrescriptionRequest(email, new CapturingLogger<SendPrescriptionRequest>());

        await FluentActions.Awaiting(() =>
                sut.ExecuteAsync(recipient, Subject, Body, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();

        email.Sent.Should().BeEmpty();
    }

    private static void AssertNoSensitiveValues(CapturingLogger<SendPrescriptionRequest> log)
    {
        foreach (var text in log.AllText())
        {
            text.Should().NotContain(Recipient);
            text.Should().NotContain("Enalapril");
            text.Should().NotContain("Rossi");
            text.Should().NotContain("Bianchi");
        }
    }

    // Simulates an SMTP error whose text repeats the recipient, as a
    // real "mailbox unavailable" reply does.
    private sealed class LeakyFailingEmailService : IEmailNotificationService
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                $"5.1.1 <{message.ExplicitRecipient}>: mailbox unavailable ({message.Subject})");

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class CancellingEmailService : IEmailNotificationService
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
