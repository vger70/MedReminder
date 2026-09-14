using MedReminder.Application.Abstractions;
using MedReminder.UI.Tray;

namespace MedReminder.UI.Notifications;

// Sostituisce BalloonTipNotificationService (Infrastructure) quando la
// UI è avviata: riusa la NotifyIcon principale così l'utente vede
// UNA sola icona tray. La lifetime è Singleton, coerente con
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
