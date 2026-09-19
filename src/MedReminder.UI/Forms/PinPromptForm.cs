using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Boot-time PIN prompt for a picked profile
// (docs/ANALYSIS-MULTI-USER.md §8). The PIN is friction against
// accidental profile switches, not protection against filesystem
// access — the "friction, not security" line is always visible,
// not hidden behind a tooltip, so the user cannot miss it (§8.2,
// polished in Increment 15e).
//
// Three wrong attempts close the dialog with DialogResult.Abort;
// the rate limit lives in memory only, matching §8.3.
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
        Width = 420;
        Height = 240;
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
            MaximumSize = new System.Drawing.Size(380, 0),
            Location = new System.Drawing.Point(16, 12),
        };

        _pinBox = new TextBox
        {
            UseSystemPasswordChar = true,
            Width = 240,
            Location = new System.Drawing.Point(16, 60),
            MaxLength = 32,
        };

        // "Friction, not security" note is always visible in the
        // dialog — hiding it behind a tooltip would understate the
        // point (§8.2).
        var frictionNote = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(380, 0),
            ForeColor = System.Drawing.Color.DarkGray,
            Location = new System.Drawing.Point(16, 92),
            Text = _loc.Get("Ui.PinPromptForm.FrictionNote"),
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = System.Drawing.Color.Firebrick,
            Location = new System.Drawing.Point(16, 130),
            Text = string.Empty,
        };

        _okButton = new Button
        {
            Text = _loc.Get("Common.Ok"),
            DialogResult = DialogResult.None,
            Location = new System.Drawing.Point(216, 168),
            Width = 80,
        };
        _cancelButton = new Button
        {
            Text = _loc.Get("Common.Cancel"),
            DialogResult = DialogResult.Cancel,
            Location = new System.Drawing.Point(304, 168),
            Width = 80,
        };
        _okButton.Click += (_, _) => Verify();
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        var tooltip = new ToolTip { ShowAlways = true };
        tooltip.SetToolTip(_pinBox, _loc.Get("Ui.PinPromptForm.Tooltip.Friction"));

        Controls.Add(prompt);
        Controls.Add(_pinBox);
        Controls.Add(frictionNote);
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
            // Explicit modal before closing — the previous 15c
            // behaviour flashed the status label for a fraction of a
            // second while Close() ran, so the user only saw the
            // window disappear (polished in 15e).
            MessageBox.Show(this,
                _loc.Get("Ui.PinPromptForm.LockedOut"),
                _loc.Get("Ui.PinPromptForm.Title", _profile.DisplayName),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.Abort;
            Close();
            return;
        }

        _statusLabel.Text = _loc.Get("Ui.PinPromptForm.Wrong", _attemptsLeft);
        _pinBox.Focus();
    }
}
