using MedReminder.Application.Notifications;

namespace MedReminder.Application.Abstractions;

public interface IEmailNotificationService
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken);
}
