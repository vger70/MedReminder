using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;

namespace MedReminder.Application.Tests.Support;

// Registrano gli invii e possono simulare fallimenti impostando ShouldFail.
internal sealed class RecordingEmailNotificationService : IEmailNotificationService
{
    public List<EmailMessage> Sent { get; } = new();
    public bool ShouldFail { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (ShouldFail) throw new InvalidOperationException("Fake SMTP failure.");
        Sent.Add(message);
        return Task.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
        => Task.FromResult(!ShouldFail);
}

internal sealed class RecordingWindowsNotificationService : IWindowsNotificationService
{
    public List<(string Title, string Body)> Sent { get; } = new();
    public bool ShouldFail { get; set; }

    public Task ShowAsync(string title, string body, CancellationToken cancellationToken)
    {
        if (ShouldFail) throw new InvalidOperationException("Fake toast failure.");
        Sent.Add((title, body));
        return Task.CompletedTask;
    }
}
