using MedReminder.Application.Abstractions;
using MedReminder.UI.Tray;

namespace MedReminder.UI.Notifications;

// Replaces BalloonTipNotificationService (Infrastructure) when the
// UI is running: reuses the main NotifyIcon so the user sees ONE
// tray icon. Lifetime is Singleton, consistent with
// ApplicationTrayIcon.
internal sealed class TrayBalloonNotificationService : IWindowsNotificationService
{
    private readonly ApplicationTrayIcon _tray;

    public TrayBalloonNotificationService(ApplicationTrayIcon tray)
    {
        _tray = tray;
    }

    public Task ShowAsync(string title, string body, CancellationToken cancellationToken)
    {
        _tray.ShowBalloon(title, body);
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}
