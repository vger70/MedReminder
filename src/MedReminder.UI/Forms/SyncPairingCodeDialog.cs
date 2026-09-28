using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync.Remote;

namespace MedReminder.UI.Forms;

// Takes a pairing code shown by a paired device (B.1 Phase 4c,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §6.1): to join a group without
// the passphrase, or to take the new key after a rotation. The device
// name is asked only when joining.
internal sealed class SyncPairingCodeDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly TextBox _code;
    private readonly TextBox? _name;

    public SyncPairingCodeDialog(ILocalizationService localization, string? defaultDeviceName)
    {
        _loc = localization;

        Text = _loc.Get("Ui.SyncDialog.PairingCode.Title");
        Width = 560;
        Height = defaultDeviceName is null ? 280 : 320;
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
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var hint = new Label
        {
            Text = _loc.Get("Ui.SyncDialog.PairingCode.Hint"),
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            Margin = new Padding(0, 0, 0, 10),
        };
        layout.Controls.Add(hint, 0, 0);
        layout.SetColumnSpan(hint, 2);

        var row = 1;
        if (defaultDeviceName is not null)
        {
            _name = new TextBox { Text = defaultDeviceName, Dock = DockStyle.Fill, MaxLength = 60 };
            AddRow(layout, row++, _loc.Get("Ui.SyncDialog.Passphrase.DeviceName"), _name);
        }
        _code = new TextBox
        {
            Multiline = true,
            WordWrap = true,
            Height = 64,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9F),
        };
        AddRow(layout, row++, _loc.Get("Ui.SyncDialog.PairingCode.Code"), _code);

        layout.RowCount = row + 1;
        for (var i = 0; i < row; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

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
        CancelButton = cancel;
    }

    public SyncPairingCode? Code { get; private set; }

    public string DeviceName => _name?.Text.Trim() ?? string.Empty;

    private void Accept()
    {
        string? error = null;
        if (_name is not null && DeviceName.Length == 0) error = _loc.Get("Ui.SyncDialog.Passphrase.Error.Name");
        else if (!SyncPairingCode.TryParse(_code.Text, out var code)) error = _loc.Get("Ui.SyncDialog.PairingCode.Invalid");
        else Code = code;

        if (error is not null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _code.Text = string.Empty;
        DialogResult = DialogResult.OK;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}
