using System.Runtime.Versioning;
using MedReminder.Application.Abstractions;
using WinFormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using WinFormsToolTipIcon = System.Windows.Forms.ToolTipIcon;
using SystemIcons = System.Drawing.SystemIcons;

namespace MedReminder.Infrastructure.Notifications;

// Local notification via NotifyIcon.ShowBalloonTip. Pragmatic choice
// for the MVP (ANALYSIS §1.1 item 9): no toast / AUMID / Start-Menu
// shortcut required for an unpackaged app; the fallback is the
// default option.
//
// NB: NotifyIcon must be Visible to display the balloon; the service
// keeps a minimal icon in the tray. The UI (Increment 6) replaces
// this registration with an implementation that reuses the main
// tray icon, avoiding two icons for the same process.
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
