using MedReminder.Application.Notifications;

namespace MedReminder.Application.Abstractions;

public interface IWindowsNotificationService
{
    Task ShowAsync(string title, string body, CancellationToken cancellationToken);

    // A notification about something the user can act on from the toast
    // (EVOLUTION-PROPOSALS-2 §3.4). Implementations without actions (the
    // tray balloon, test doubles) show it as a plain notification.
    Task ShowAsync(string title, string body, NotificationTarget target, CancellationToken cancellationToken)
        => ShowAsync(title, body, cancellationToken);
}
