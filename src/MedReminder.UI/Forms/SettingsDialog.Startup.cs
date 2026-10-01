using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Export;
using MedReminder.Application.UseCases;
using MedReminder.Infrastructure.Email;
using MedReminder.Infrastructure.Settings;
using MedReminder.Infrastructure.Storage;
using MedReminder.UI.Controls;
using MedReminder.UI.UiExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Forms;

// Settings → Startup (the section list and the dialog frame are in
// SettingsDialog.cs).
internal sealed partial class SettingsDialog
{
    // Startup section.
    private Panel BuildStartupTab()
    {
        var page = new Panel();

        _autoStartCheck = new CheckBox
        {
            Text = _loc.Get("Ui.SettingsDialog.Startup.AutoStart"),
            AutoSize = true,
            Checked = _autoStart.IsEnabled,
        };
        _autoStartCheck.CheckedChanged += (_, _) =>
        {
            try
            {
                if (_autoStartCheck.Checked) _autoStart.Enable();
                else _autoStart.Disable();
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message,
                    _loc.Get("Ui.SettingsDialog.Startup.Error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _autoStartCheck.Checked = _autoStart.IsEnabled;
            }
        };

        var note = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.SettingsDialog.Startup.Note"),
            ForeColor = UiColors.Hint,
        };

        // A top-down flow wraps into a second column when the tab is
        // shorter than its content (Large text, 150 % scaling); it
        // scrolls instead.
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            WrapContents = false,
            AutoScroll = true,
        };
        panel.Controls.Add(_autoStartCheck);
        panel.Controls.Add(note);
        page.Controls.Add(panel);
        return page;
    }
}
