using System.Runtime.Versioning;
using MedReminder.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace MedReminder.UI.Notifications;

// Toast Windows moderne via CommunityToolkit (Microsoft.Toolkit.Uwp.
// Notifications). ToastContentBuilder + Show() gestiscono
// automaticamente la registrazione dell'AUMID a runtime per le app
// unpackaged (crea uno shortcut nello Start Menu + registrazione COM
// alla prima esecuzione). Non serve fare setup manuale.
//
// Sostituisce (in DI) il TrayBalloonNotificationService come primary
// implementation di IWindowsNotificationService. In caso di errore
// (Windows 10 pre-1809, feature disabilitata da policy, ecc.) ricade
// automaticamente sul balloon della tray icon per non perdere la
// notifica.
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
                    "Toast moderno non disponibile; passo al balloon della tray icon per il resto della sessione.");
                _toastAvailable = false;
                // fall through al fallback
            }
        }

        await _fallback.ShowAsync(title, body, cancellationToken);
    }
}
