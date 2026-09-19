using System.Windows.Forms;
using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Modal dialog to set, replace or clear a profile PIN. Shared between
// ProfilesManagerForm (admin action on any profile) and
// SettingsDialog.Notifications (self-service on the caller's own
// profile). The dialog does not know which profile is being edited
// nor who is calling — it only collects the intent (clear vs new
// value) and returns it to the caller for persistence.
internal sealed class ChangePinDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly TextBox _pinBox;
    private readonly TextBox _pinConfirmBox;
    private readonly CheckBox _clearBox;
    private readonly Label _statusLabel;

    public ChangePinDialog(ILocalizationService loc, bool profileHasPin)
    {
        _loc = loc;
        Text = _loc.Get("Ui.ProfilesManagerForm.PinDialog.Title");
        Width = 420;
        Height = 260;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var prompt = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(380, 0),
            Location = new System.Drawing.Point(16, 12),
            Text = _loc.Get("Ui.ProfilesManagerForm.PinDialog.Prompt"),
        };
        _pinBox = new TextBox
        {
            Location = new System.Drawing.Point(16, 60),
            Width = 180,
            UseSystemPasswordChar = true,
            MaxLength = 32,
            PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinPlaceholder"),
        };
        _pinConfirmBox = new TextBox
        {
            Location = new System.Drawing.Point(206, 60),
            Width = 180,
            UseSystemPasswordChar = true,
            MaxLength = 32,
            PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinConfirmPlaceholder"),
        };
        _clearBox = new CheckBox
        {
            AutoSize = true,
            Location = new System.Drawing.Point(16, 96),
            Text = _loc.Get("Ui.ProfilesManagerForm.PinDialog.Clear"),
            Enabled = profileHasPin,
        };
        _clearBox.CheckedChanged += (_, _) =>
        {
            var enabled = !_clearBox.Checked;
            _pinBox.Enabled = enabled;
            _pinConfirmBox.Enabled = enabled;
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = System.Drawing.Color.Firebrick,
            Location = new System.Drawing.Point(16, 128),
            Text = string.Empty,
        };

        var okButton = new Button
        {
            Text = _loc.Get("Common.Ok"),
            Location = new System.Drawing.Point(216, 180),
            Width = 90,
        };
        var cancelButton = new Button
        {
            Text = _loc.Get("Common.Cancel"),
            DialogResult = DialogResult.Cancel,
            Location = new System.Drawing.Point(316, 180),
            Width = 80,
        };
        okButton.Click += (_, _) => Confirm();
        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.Add(prompt);
        Controls.Add(_pinBox);
        Controls.Add(_pinConfirmBox);
        Controls.Add(_clearBox);
        Controls.Add(_statusLabel);
        Controls.Add(okButton);
        Controls.Add(cancelButton);

        var tooltip = new ToolTip { ShowAlways = true };
        tooltip.SetToolTip(_clearBox,
            _loc.Get("Ui.ProfilesManagerForm.PinDialog.ClearTooltip"));
    }

    public bool ClearPin { get; private set; }
    public string NewPin { get; private set; } = string.Empty;

    private void Confirm()
    {
        if (_clearBox.Checked)
        {
            ClearPin = true;
            NewPin = string.Empty;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        var pin = _pinBox.Text;
        if (string.IsNullOrEmpty(pin))
        {
            _statusLabel.Text = _loc.Get("Ui.PinPromptForm.Empty");
            _pinBox.Focus();
            return;
        }
        if (!string.Equals(pin, _pinConfirmBox.Text, StringComparison.Ordinal))
        {
            _statusLabel.Text = _loc.Get("Ui.FirstRunWizardForm.PinMismatch");
            _pinConfirmBox.Focus();
            return;
        }
        ClearPin = false;
        NewPin = pin;
        DialogResult = DialogResult.OK;
        Close();
    }
}
