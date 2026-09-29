using System.Runtime.Versioning;
using MedReminder.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace MedReminder.UI.Notifications;

// Modern Windows toasts via CommunityToolkit
// (Microsoft.Toolkit.Uwp.Notifications). ToastContentBuilder + Show()
// handle AUMID registration at runtime for unpackaged apps
// automatically (creates a Start-Menu shortcut + COM registration on
// first execution). No manual setup required.
//
// Replaces (in DI) TrayBalloonNotificationService as the primary
// IWindowsNotificationService implementation. On error (Windows 10
// pre-1809, feature disabled by policy, etc.) it falls back
// automatically to the tray icon's balloon so the notification is
// not lost.
[SupportedOSPlatform("windows10.0.17763.0")]
internal sealed class ToastWindowsNotificationService : IWindowsNotificationService
{
    private readonly TrayBalloonNotificationService _fallback;
    private readonly ILogger<ToastWindowsNotificationService> _log;
    private bool _toastAvailable = true;

    public ToastWindowsNotificationService(
        TrayBalloonNotificationService fallback,
        ILogger<ToastWindowsNotificationService> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public async Task ShowAsync(string title, string body, CancellationToken cancellationToken)
    {
        if (_toastAvailable)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(body)
                    .Show();
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Modern toast unavailable; switching to the tray-icon balloon for the rest of the session.");
                _toastAvailable = false;
                // fall through to the fallback
            }
        }

        await _fallback.ShowAsync(title, body, cancellationToken);
    }
}
