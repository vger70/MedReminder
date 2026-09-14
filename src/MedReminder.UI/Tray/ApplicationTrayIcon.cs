using System.Drawing;
using System.Windows.Forms;

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

    public ApplicationTrayIcon()
    {
        var menu = new ContextMenuStrip();
        OpenItem = new ToolStripMenuItem("Apri MedReminder");
        CheckNowItem = new ToolStripMenuItem("Controlla ora");
        SettingsItem = new ToolStripMenuItem("Impostazioni…");
        ExitItem = new ToolStripMenuItem("Esci");

        menu.Items.Add(OpenItem);
        menu.Items.Add(CheckNowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(SettingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(ExitItem);

        NotifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
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
