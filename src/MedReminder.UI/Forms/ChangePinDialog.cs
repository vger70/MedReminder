using MedReminder.Application.Abstractions;
using MedReminder.UI.UiExtensions;

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
        Height = 240;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        DialogLayout.GrowToContent(this);

        var prompt = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(380, 0),
            Margin = new Padding(0, 0, 0, UiTheme.Space.S),
            Text = _loc.Get("Ui.ProfilesManagerForm.PinDialog.Prompt"),
        };
        _pinBox = new TextBox
        {
            Width = 180,
            UseSystemPasswordChar = true,
            MaxLength = 32,
            PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinPlaceholder"),
            Margin = new Padding(0, 0, UiTheme.Space.S, 0),
        };
        _pinConfirmBox = new TextBox
        {
            Width = 180,
            UseSystemPasswordChar = true,
            MaxLength = 32,
            PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinConfirmPlaceholder"),
            Margin = Padding.Empty,
        };
        var pinRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, UiTheme.Space.S),
        };
        pinRow.Controls.Add(_pinBox);
        pinRow.Controls.Add(_pinConfirmBox);
        _clearBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ProfilesManagerForm.PinDialog.Clear"),
            Enabled = profileHasPin,
        };
        _clearBox.CheckedChanged += (_, _) =>
        {
            var enabled = !_clearBox.Checked;
            _pinBox.Enabled = enabled;
            _pinConfirmBox.Enabled = enabled;
        };

        // Inline error under the fields (F9).
        _statusLabel = DialogLayout.ErrorLabel();

        var okButton = DialogLayout.Button(_loc.Get("Common.Ok"));
        var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
        okButton.Click += (_, _) => Confirm();

        var content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.M, UiTheme.Space.L, 0),
        };
        content.Controls.Add(prompt);
        content.Controls.Add(pinRow);
        content.Controls.Add(_clearBox);
        content.Controls.Add(_statusLabel);

        Controls.Add(content);
        Controls.Add(DialogLayout.ButtonBar(this, okButton, cancelButton));

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
            DialogLayout.ShowError(_statusLabel, _loc.Get("Ui.PinPromptForm.Empty"), _pinBox);
            return;
        }
        if (!string.Equals(pin, _pinConfirmBox.Text, StringComparison.Ordinal))
        {
            DialogLayout.ShowError(_statusLabel, _loc.Get("Ui.FirstRunWizardForm.PinMismatch"), _pinConfirmBox);
            return;
        }
        ClearPin = false;
        NewPin = pin;
        DialogResult = DialogResult.OK;
        Close();
    }
}
