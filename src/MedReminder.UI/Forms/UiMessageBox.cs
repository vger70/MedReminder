using System.Media;
using MedReminder.Application.Abstractions;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Message box drawn by MedReminder (docs/analysis/ANALYSIS-UI-MODERNIZATION.md
// §5.3, F10). The Windows MessageBox stays light in dark mode and labels
// its buttons in the language of Windows; this one follows the theme and
// the dictionaries. Same overloads and result as MessageBox.Show, so a
// call changes only its class name. Program keeps the Windows MessageBox
// for the start-up and crash paths, where nothing of the UI may be ready.
internal sealed class UiMessageBox : MedReminderFormBase
{
    private const int TextWidth = 440;

    // Set by Program once the dictionaries are loaded; English captions
    // until then.
    internal static ILocalizationService? Localization { get; set; }

    private readonly MessageBoxIcon _icon;
    private readonly string _copyText;

    private UiMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
    {
        _icon = icon;
        _copyText = caption + Environment.NewLine + Environment.NewLine + text;
        Text = caption;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Width = 420;
        Height = 160;
        DialogLayout.GrowToContent(this);

        var message = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(TextWidth, 0),
            UseMnemonic = false,
            Margin = new Padding(0, UiTheme.Space.XS, 0, 0),
        };

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.L, UiTheme.Space.L, UiTheme.Space.S),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (IconGlyph(icon) is { } glyph)
        {
            var size = ScaledIconSize(32);
            body.Controls.Add(new PictureBox
            {
                Image = Mdl2Glyph.Create(glyph.Glyph, size, glyph.Color),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 0, UiTheme.Space.M, 0),
            }, 0, 0);
        }
        body.Controls.Add(message, 1, 0);

        // Buttons left to right as in Windows (Yes, No, Cancel; OK,
        // Cancel), at the bottom right like every dialog bar (§5.3).
        var choices = ButtonsFor(buttons);
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.M, UiTheme.Space.L, UiTheme.Space.L),
        };
        for (var i = choices.Count - 1; i >= 0; i--) row.Controls.Add(choices[i]);

        var defaultIndex = defaultButton switch
        {
            MessageBoxDefaultButton.Button2 => 1,
            MessageBoxDefaultButton.Button3 => 2,
            _ => 0,
        };
        AcceptButton = choices[Math.Min(defaultIndex, choices.Count - 1)];
        // Esc answers Cancel when there is one, otherwise No or OK, as the
        // Windows MessageBox does.
        CancelButton = choices.FirstOrDefault(b => b.DialogResult == DialogResult.Cancel)
            ?? choices.FirstOrDefault(b => b.DialogResult == DialogResult.No)
            ?? choices[^1];

        Controls.Add(body);
        Controls.Add(row);

        Shown += (_, _) =>
        {
            ((Control)AcceptButton).Focus();
            SoundFor(_icon)?.Play();
        };
    }

    public static DialogResult Show(string text)
        => Show(null, text, string.Empty);

    public static DialogResult Show(string text, string caption)
        => Show(null, text, caption);

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
        => Show(null, text, caption, buttons);

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => Show(null, text, caption, buttons, icon);

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
        => Show(null, text, caption, buttons, icon, defaultButton);

    public static DialogResult Show(IWin32Window? owner, string text)
        => Show(owner, text, string.Empty);

    public static DialogResult Show(IWin32Window? owner, string text, string caption)
        => Show(owner, text, caption, MessageBoxButtons.OK);

    public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons)
        => Show(owner, text, caption, buttons, MessageBoxIcon.None);

    public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons,
        MessageBoxIcon icon, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        if (buttons is not (MessageBoxButtons.OK or MessageBoxButtons.OKCancel
            or MessageBoxButtons.YesNo or MessageBoxButtons.YesNoCancel))
        {
            return owner is null
                ? MessageBox.Show(text, caption, buttons, icon, defaultButton)
                : MessageBox.Show(owner, text, caption, buttons, icon, defaultButton);
        }
        using var box = new UiMessageBox(text, caption, buttons, icon, defaultButton);
        box.StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        return owner is null ? box.ShowDialog() : box.ShowDialog(owner);
    }

    // Ctrl+C copies the caption and the text, as in the Windows MessageBox.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.C))
        {
            try { Clipboard.SetText(_copyText); } catch (System.Runtime.InteropServices.ExternalException) { }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static List<Button> ButtonsFor(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.OKCancel => [Make("Common.Ok", "OK", DialogResult.OK), Make("Common.Cancel", "Cancel", DialogResult.Cancel)],
        MessageBoxButtons.YesNo => [Make("Common.Yes", "Yes", DialogResult.Yes), Make("Common.No", "No", DialogResult.No)],
        MessageBoxButtons.YesNoCancel =>
            [Make("Common.Yes", "Yes", DialogResult.Yes), Make("Common.No", "No", DialogResult.No), Make("Common.Cancel", "Cancel", DialogResult.Cancel)],
        _ => [Make("Common.Ok", "OK", DialogResult.OK)],
    };

    private static Button Make(string key, string fallback, DialogResult result)
        => DialogLayout.Button(Localization?.Get(key) ?? fallback, result);

    private static (string Glyph, Color Color)? IconGlyph(MessageBoxIcon icon)
    {
        var p = UiTheme.Palette;
        return icon switch
        {
            MessageBoxIcon.Error => (Mdl2Glyph.Glyphs.Cancel, p.DangerText),
            MessageBoxIcon.Warning => (Mdl2Glyph.Glyphs.Warning, p.WarningText),
            MessageBoxIcon.Information => (Mdl2Glyph.Glyphs.Info, p.Accent),
            MessageBoxIcon.Question => (Mdl2Glyph.Glyphs.Help, p.Accent),
            _ => null,
        };
    }

    private static SystemSound? SoundFor(MessageBoxIcon icon) => icon switch
    {
        MessageBoxIcon.Error => SystemSounds.Hand,
        MessageBoxIcon.Warning => SystemSounds.Exclamation,
        MessageBoxIcon.Information => SystemSounds.Asterisk,
        MessageBoxIcon.Question => SystemSounds.Question,
        _ => null,
    };
}
