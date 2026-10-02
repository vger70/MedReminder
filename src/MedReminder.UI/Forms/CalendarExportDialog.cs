using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Therapy → Export to calendar… (docs/notes/EVOLUTION-PROPOSALS-2.md
// §3.7): writes an .ics file with the dates MedReminder knows, for
// Outlook, Google Calendar or a phone. Titles are generic unless the
// user ticks the names, every time: calendars usually live on a
// third-party cloud. The file is written only where the user chooses;
// neither its content nor the path is logged.
internal sealed class CalendarExportDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly Func<bool, Task<string>> _build;
    private readonly CheckBox _includeNames;

    public CalendarExportDialog(Func<bool, Task<string>> build, ILocalizationService localization)
    {
        _loc = localization;
        _build = build;

        Text = _loc.Get("Ui.CalendarExportDialog.Title");
        Width = 560;
        Height = 300;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        DialogLayout.GrowToContent(this);

        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Text = _loc.Get("Ui.CalendarExportDialog.Hint"),
        };
        _includeNames = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.CalendarExportDialog.IncludeNames"),
        };
        var privacy = new Label
        {
            AutoSize = true,
            ForeColor = UiColors.Hint,
            MaximumSize = new Size(500, 0),
            Text = _loc.Get("Ui.CalendarExportDialog.Privacy"),
        };

        var exportButton = DialogLayout.Button(_loc.Get("Ui.CalendarExportDialog.Export"));
        var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
        exportButton.Click += async (_, _) => await ExportAsync();
        var buttons = DialogLayout.ButtonBar(this, exportButton, cancelButton);

        Controls.Add(DialogLayout.Stack(hint, _includeNames, privacy));
        Controls.Add(buttons);
    }

    private async Task ExportAsync()
    {
        using var save = new SaveFileDialog
        {
            Title = _loc.Get("Ui.CalendarExportDialog.SaveDialog.Title"),
            Filter = _loc.Get("Ui.CalendarExportDialog.SaveDialog.Filter"),
            DefaultExt = "ics",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"MedReminder-{DateTime.Now:yyyyMMdd}.ics",
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var ics = await _build(_includeNames.Checked);
            await File.WriteAllTextAsync(save.FileName, ics, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, _loc.Get("Ui.CalendarExportDialog.Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        UiMessageBox.Show(this, _loc.Get("Ui.CalendarExportDialog.Done"), Text,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}
