using System.Windows.Forms;
using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Boot-time PIN prompt for a picked profile
// (docs/ANALYSIS-MULTI-USER.md §8). Introduced in Increment 15c so
// the picker can gate profiles that carry a PIN; the polish pass
// (tooltips, refined copy) lands in 15e together with the
// user-guide entries.
//
// The PIN is friction against accidental profile switches, not
// protection against filesystem access — see §8.2. The rate limit
// (three wrong attempts before the form gives up) lives in-memory
// only, exactly as the design requires.
internal sealed class PinPromptForm : MedReminderFormBase
{
    private const int MaxAttempts = 3;

    private readonly IProfileRegistry _registry;
    private readonly ILocalizationService _loc;
    private readonly Profile _profile;
    private readonly TextBox _pinBox;
    private readonly Label _statusLabel;
    private readonly Button _okButton;
    private readonly Button _cancelButton;
    private int _attemptsLeft = MaxAttempts;

    public PinPromptForm(IProfileRegistry registry, Profile profile, ILocalizationService loc)
    {
        _registry = registry;
        _profile = profile;
        _loc = loc;

        Text = _loc.Get("Ui.PinPromptForm.Title", profile.DisplayName);
        Width = 380;
        Height = 200;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var prompt = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.PinPromptForm.Prompt", profile.DisplayName),
            MaximumSize = new System.Drawing.Size(340, 0),
            Location = new System.Drawing.Point(16, 12),
        };

        _pinBox = new TextBox
        {
            UseSystemPasswordChar = true,
            Width = 200,
            Location = new System.Drawing.Point(16, 60),
            MaxLength = 32,
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = System.Drawing.Color.Firebrick,
            Location = new System.Drawing.Point(16, 92),
            Text = string.Empty,
        };

        _okButton = new Button
        {
            Text = _loc.Get("Common.Ok"),
            DialogResult = DialogResult.None,
            Location = new System.Drawing.Point(180, 128),
            Width = 80,
        };
        _cancelButton = new Button
        {
            Text = _loc.Get("Common.Cancel"),
            DialogResult = DialogResult.Cancel,
            Location = new System.Drawing.Point(264, 128),
            Width = 80,
        };
        _okButton.Click += (_, _) => Verify();
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        var tooltip = new ToolTip { ShowAlways = true };
        tooltip.SetToolTip(_pinBox, _loc.Get("Ui.PinPromptForm.Tooltip.Friction"));

        Controls.Add(prompt);
        Controls.Add(_pinBox);
        Controls.Add(_statusLabel);
        Controls.Add(_okButton);
        Controls.Add(_cancelButton);

        Shown += (_, _) => _pinBox.Focus();
    }

    // The verified PIN result — DialogResult.OK when accepted,
    // DialogResult.Cancel when the user aborts, DialogResult.Abort
    // after three failed attempts (used by the caller to bail out
    // of the boot flow).
    public new DialogResult DialogResult
    {
        get => base.DialogResult;
        private set => base.DialogResult = value;
    }

    private void Verify()
    {
        var pin = _pinBox.Text;
        if (string.IsNullOrEmpty(pin))
        {
            _statusLabel.Text = _loc.Get("Ui.PinPromptForm.Empty");
            return;
        }

        if (_registry.VerifyPin(_profile.Id, pin))
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _attemptsLeft--;
        _pinBox.Clear();
        if (_attemptsLeft <= 0)
        {
            _statusLabel.Text = _loc.Get("Ui.PinPromptForm.LockedOut");
            DialogResult = DialogResult.Abort;
            Close();
            return;
        }

        _statusLabel.Text = _loc.Get("Ui.PinPromptForm.Wrong", _attemptsLeft);
        _pinBox.Focus();
    }
}
