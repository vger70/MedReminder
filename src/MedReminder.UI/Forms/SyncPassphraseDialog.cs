using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Device name and sync passphrase (B.1 Phase 3d). When creating a group
// the passphrase is typed twice; when joining once. The passphrase is
// returned as a char array the caller zeroes after use.
internal sealed class SyncPassphraseDialog : MedReminderFormBase
{
    // Long enough to resist guessing on a stolen copy of the folder: the
    // wrap is protected by the passphrase alone (§5.4).
    public const int MinimumLength = 10;

    private readonly ILocalizationService _loc;
    private readonly bool _confirm;
    private readonly TextBox _name;
    private readonly TextBox _passphrase;
    private readonly TextBox? _repeat;

    public SyncPassphraseDialog(ILocalizationService localization, bool confirm, string defaultDeviceName)
    {
        _loc = localization;
        _confirm = confirm;

        Text = _loc.Get(confirm ? "Ui.SyncDialog.Passphrase.CreateTitle" : "Ui.SyncDialog.Passphrase.JoinTitle");
        Width = 520;
        Height = confirm ? 340 : 290;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.75F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var hint = new Label
        {
            Text = _loc.Get(confirm ? "Ui.SyncDialog.Passphrase.CreateHint" : "Ui.SyncDialog.Passphrase.JoinHint"),
            AutoSize = true,
            MaximumSize = new Size(470, 0),
            Margin = new Padding(0, 0, 0, 10),
        };
        layout.Controls.Add(hint, 0, 0);
        layout.SetColumnSpan(hint, 2);

        _name = new TextBox { Text = defaultDeviceName, Dock = DockStyle.Fill, MaxLength = 60 };
        AddRow(layout, 1, _loc.Get("Ui.SyncDialog.Passphrase.DeviceName"), _name);
        _passphrase = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
        AddRow(layout, 2, _loc.Get("Ui.SyncDialog.Passphrase.Passphrase"), _passphrase);
        if (confirm)
        {
            _repeat = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            AddRow(layout, 3, _loc.Get("Ui.SyncDialog.Passphrase.Repeat"), _repeat);
        }

        var ok = new Button { Text = _loc.Get("Common.Ok"), AutoSize = true, Height = 32 };
        ok.Click += (_, _) => Accept();
        var cancel = new Button
        {
            Text = _loc.Get("Common.Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, Height = 32,
        };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string DeviceName => _name.Text.Trim();

    public char[] TakePassphrase()
    {
        var value = _passphrase.Text.ToCharArray();
        _passphrase.Text = string.Empty;
        if (_repeat is not null) _repeat.Text = string.Empty;
        return value;
    }

    private void Accept()
    {
        string? error = null;
        if (DeviceName.Length == 0) error = _loc.Get("Ui.SyncDialog.Passphrase.Error.Name");
        else if (_passphrase.TextLength < MinimumLength)
            error = _loc.Get("Ui.SyncDialog.Passphrase.Error.Short", MinimumLength);
        else if (_confirm && _repeat!.Text != _passphrase.Text)
            error = _loc.Get("Ui.SyncDialog.Passphrase.Error.Mismatch");

        if (error is not null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}
