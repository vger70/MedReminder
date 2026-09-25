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
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);
        // Layout panels + AutoSize instead of absolute coordinates:
        // the friction note wraps on two or three lines in some
        // languages, and a fixed-height form cut the buttons off.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var contentWidth = LogicalToDeviceUnits(380);

        var prompt = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.PinPromptForm.Prompt", profile.DisplayName),
            MaximumSize = new System.Drawing.Size(contentWidth, 0),
            Margin = new Padding(3, 3, 3, 8),
        };

        _pinBox = new TextBox
        {
            UseSystemPasswordChar = true,
            Width = LogicalToDeviceUnits(240),
            MaxLength = 32,
            Margin = new Padding(3, 3, 3, 8),
        };

        // "Friction, not security" note is always visible in the
        // dialog — hiding it behind a tooltip would understate the
        // point (§8.2).
        var frictionNote = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(contentWidth, 0),
            ForeColor = System.Drawing.Color.DarkGray,
            Text = _loc.Get("Ui.PinPromptForm.FrictionNote"),
            Margin = new Padding(3, 3, 3, 8),
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(contentWidth, 0),
            ForeColor = System.Drawing.Color.Firebrick,
            Text = string.Empty,
        };

        _okButton = new Button
        {
            Text = _loc.Get("Common.Ok"),
            DialogResult = DialogResult.None,
            AutoSize = true,
            MinimumSize = new System.Drawing.Size(LogicalToDeviceUnits(96), LogicalToDeviceUnits(34)),
            Padding = new Padding(8, 2, 8, 2),
        };
        _cancelButton = new Button
        {
            Text = _loc.Get("Common.Cancel"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new System.Drawing.Size(LogicalToDeviceUnits(96), LogicalToDeviceUnits(34)),
            Padding = new Padding(8, 2, 8, 2),
        };
        _okButton.Click += (_, _) => Verify();
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        // RightToLeft: the first control added sits on the far right,
        // so Cancel is added first to keep the [OK] [Cancel] order.
        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 12, 3, 3),
        };
        buttonRow.Controls.Add(_cancelButton);
        buttonRow.Controls.Add(_okButton);

        var tooltip = new ToolTip { ShowAlways = true };
        tooltip.SetToolTip(_pinBox, _loc.Get("Ui.PinPromptForm.Tooltip.Friction"));

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
        };
        foreach (var control in new Control[]
                 { prompt, _pinBox, frictionNote, _statusLabel, buttonRow })
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control);
        }
        Controls.Add(layout);

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
