using System.Runtime.Versioning;
using MedReminder.Application.Abstractions;
using WinFormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using WinFormsToolTipIcon = System.Windows.Forms.ToolTipIcon;
using SystemIcons = System.Drawing.SystemIcons;

namespace MedReminder.Infrastructure.Notifications;

// Notifica locale via NotifyIcon.ShowBalloonTip. Scelta pragmatica per
// l'MVP (ANALYSIS §1.1 punto 9): niente Toast/AUMID/shortcut Start Menu
// necessari per un'app unpackaged; il fallback è la default option.
//
// NB: NotifyIcon deve essere Visible per mostrare il balloon; il servizio
// mantiene un'icona minima nella tray. La UI (Incremento 6) sostituirà
// questa registrazione con un'implementazione che riusa la tray-icon
// principale, evitando due icone diverse per lo stesso processo.
[SupportedOSPlatform("windows")]
internal sealed class BalloonTipNotificationService
    : IWindowsNotificationService, IDisposable
{
    private const int BalloonTimeoutMilliseconds = 5000;
    private readonly WinFormsNotifyIcon _notifyIcon;

    public BalloonTipNotificationService()
    {
        _notifyIcon = new WinFormsNotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "MedReminder",
            Visible = true,
        };
    }

    public Task ShowAsync(string title, string body, CancellationToken cancellationToken)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = body;
        _notifyIcon.BalloonTipIcon = WinFormsToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(BalloonTimeoutMilliseconds);
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
