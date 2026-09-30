using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Household step H3d (docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md
// §6.2, §6.3 page 5): the profiles a new device will hold, none selected
// at first. With administrators given, it is the approval on the joining
// device: an administrator of the household and its PIN, then the
// profiles. The PIN is checked by the caller (JoinInstallation).
internal sealed class HouseholdProfilesDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly CheckedListBox _profiles;
    private readonly ComboBox? _admin;
    private readonly TextBox? _pin;

    public HouseholdProfilesDialog(ILocalizationService localization, string titleKey, string hintKey,
        IReadOnlyList<HouseholdProfileChoice> profiles, IReadOnlyList<HouseholdProfileChoice>? administrators = null)
    {
        _loc = localization;

        Text = _loc.Get(titleKey);
        Width = 520;
        Height = administrators is null ? 420 : 500;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var hint = new Label
        {
            Text = _loc.Get(hintKey),
            AutoSize = true,
            MaximumSize = new Size(470, 0),
            Margin = new Padding(0, 0, 0, 10),
        };
        layout.Controls.Add(hint, 0, 0);
        layout.SetColumnSpan(hint, 2);

        var row = 1;
        if (administrators is not null)
        {
            _admin = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, DisplayMember = nameof(HouseholdProfileChoice.Name) };
            foreach (var admin in administrators) _admin.Items.Add(admin);
            if (_admin.Items.Count > 0) _admin.SelectedIndex = 0;
            AddRow(layout, row++, _loc.Get("Ui.HouseholdDialog.Approve.Admin"), _admin);
            _pin = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill, MaxLength = 32 };
            AddRow(layout, row++, _loc.Get("Ui.HouseholdDialog.Approve.Pin"), _pin);
        }

        _profiles = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, DisplayMember = nameof(HouseholdProfileChoice.Name), Height = 200 };
        foreach (var profile in profiles) _profiles.Items.Add(profile, false);
        var label = new Label { Text = _loc.Get("Ui.HouseholdDialog.Profiles"), AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
        layout.Controls.Add(label, 0, row++);
        layout.SetColumnSpan(label, 2);
        layout.Controls.Add(_profiles, 0, row);
        layout.SetColumnSpan(_profiles, 2);
        row++;

        layout.RowCount = row;
        for (var i = 0; i < row - 1; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var ok = new Button { Text = _loc.Get("Common.Ok"), AutoSize = true, Height = 32 };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Text = _loc.Get("Common.Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, Height = 32 };
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

    public IReadOnlyList<string> SelectedProfileIds { get; private set; } = [];

    public string? AdminProfileId { get; private set; }

    // Null when no PIN was typed. The caller forgets it after use.
    public string? Pin { get; private set; }

    private void Accept()
    {
        var selected = _profiles.CheckedItems.Cast<HouseholdProfileChoice>().Select(p => p.Id).ToList();
        string? error = null;
        if (_admin is not null && _admin.SelectedItem is not HouseholdProfileChoice) error = _loc.Get("Ui.HouseholdDialog.Approve.NoAdmin");
        else if (selected.Count == 0) error = _loc.Get("Ui.HouseholdDialog.Profiles.NoneSelected");
        if (error is not null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SelectedProfileIds = selected;
        AdminProfileId = (_admin?.SelectedItem as HouseholdProfileChoice)?.Id;
        Pin = string.IsNullOrEmpty(_pin?.Text) ? null : _pin.Text;
        if (_pin is not null) _pin.Text = string.Empty;
        DialogResult = DialogResult.OK;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}

internal sealed record HouseholdProfileChoice(string Id, string Name);
