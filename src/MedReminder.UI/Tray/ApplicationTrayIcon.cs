using System.Drawing;
using System.Windows.Forms;
using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Tray;

// Icona tray singleton condivisa: MainForm la usa per il menu (Apri /
// Controlla ora / Impostazioni / Esci), TrayBalloonNotificationService
// la usa per emettere balloon. Un solo NotifyIcon per il processo:
// niente doppie icone in tray (ANALYSIS §2.9).
internal sealed class ApplicationTrayIcon : IDisposable
{
    public NotifyIcon NotifyIcon { get; }
    public ToolStripMenuItem OpenItem { get; }
    public ToolStripMenuItem CheckNowItem { get; }
    public ToolStripMenuItem SettingsItem { get; }
    public ToolStripMenuItem ExitItem { get; }

    public ApplicationTrayIcon(ILocalizationService loc)
    {
        var menu = new ContextMenuStrip();
        OpenItem = new ToolStripMenuItem(loc.Get("Ui.Tray.Open"));
        CheckNowItem = new ToolStripMenuItem(loc.Get("Ui.Tray.CheckNow"));
        SettingsItem = new ToolStripMenuItem(loc.Get("Ui.Tray.Settings"));
        ExitItem = new ToolStripMenuItem(loc.Get("Ui.Tray.Exit"));

        menu.Items.Add(OpenItem);
        menu.Items.Add(CheckNowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(SettingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(ExitItem);

        NotifyIcon = new NotifyIcon
        {
            // AppIcon.Default può essere null se l'embedded resource
            // manca (build corrotta): fallback a SystemIcons.Information
            // per non lasciare la tray senza icona.
            Icon = AppIcon.Default ?? SystemIcons.Information,
            Text = "MedReminder",
            Visible = true,
            ContextMenuStrip = menu,
        };
    }

    public void ShowBalloon(string title, string body)
    {
        NotifyIcon.BalloonTipTitle = title;
        NotifyIcon.BalloonTipText = body;
        NotifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        NotifyIcon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        NotifyIcon.Visible = false;
        NotifyIcon.ContextMenuStrip?.Dispose();
        NotifyIcon.Dispose();
    }
}
