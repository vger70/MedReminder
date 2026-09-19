using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Mandatory first-run wizard (docs/ANALYSIS-MULTI-USER.md §12.3,
// decision §14 E). Shown at boot when the registry is empty. The
// first profile is created as Admin — no radio, no choice — because
// there must always be one admin (§1.1a) and the wizard has nothing
// to compare against.
//
// The PIN is optional — the wizard collects one only when the user
// types it in, with a hint that it is recommended for admins
// (§12.3 step 4). The polish pass (tooltips, wording, extra help
// text) lands in 15e.
internal sealed class FirstRunWizardForm : MedReminderFormBase
{
    private readonly IProfileRegistry _registry;
    private readonly ILocalizationService _loc;
    private readonly TextBox _nameBox;
    private readonly TextBox _pinBox;
    private readonly TextBox _pinConfirmBox;
    private readonly Label _statusLabel;
    private readonly Button _createButton;

    public FirstRunWizardForm(IProfileRegistry registry, ILocalizationService loc)
    {
        _registry = registry;
        _loc = loc;

        Text = _loc.Get("Ui.FirstRunWizardForm.Title");
        Width = 480;
        Height = 380;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        // The wizard cannot be closed with the X — closing it would
        // leave the app without a profile to open. AcceptButton
        // handles the flow; the Cancel button below exits the app.
        ControlBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var welcome = new Label
        {
            AutoSize = true,
            Location = new System.Drawing.Point(16, 12),
            MaximumSize = new System.Drawing.Size(440, 0),
            Font = new System.Drawing.Font("Segoe UI", 10.5F, System.Drawing.FontStyle.Bold),
            Text = _loc.Get("Ui.FirstRunWizardForm.Welcome"),
        };

        var explanation = new Label
        {
            AutoSize = true,
            Location = new System.Drawing.Point(16, 44),
            MaximumSize = new System.Drawing.Size(440, 0),
            Text = _loc.Get("Ui.FirstRunWizardForm.Explanation"),
        };

        var nameLabel = new Label
        {
            AutoSize = true,
            Location = new System.Drawing.Point(16, 128),
            Text = _loc.Get("Ui.FirstRunWizardForm.Name"),
        };
        _nameBox = new TextBox
        {
            Location = new System.Drawing.Point(16, 148),
            Width = 440,
            MaxLength = 100,
        };

        var pinLabel = new Label
        {
            AutoSize = true,
            Location = new System.Drawing.Point(16, 184),
            Text = _loc.Get("Ui.FirstRunWizardForm.PinOptional"),
        };
        _pinBox = new TextBox
        {
            Location = new System.Drawing.Point(16, 204),
            Width = 210,
            UseSystemPasswordChar = true,
            MaxLength = 32,
            PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinPlaceholder"),
        };
        _pinConfirmBox = new TextBox
        {
            Location = new System.Drawing.Point(240, 204),
            Width = 216,
            UseSystemPasswordChar = true,
            MaxLength = 32,
            PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinConfirmPlaceholder"),
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            Location = new System.Drawing.Point(16, 240),
            ForeColor = System.Drawing.Color.Firebrick,
            Text = string.Empty,
        };

        _createButton = new Button
        {
            Text = _loc.Get("Ui.FirstRunWizardForm.Create"),
            Location = new System.Drawing.Point(280, 300),
            Width = 100,
        };
        var exitButton = new Button
        {
            Text = _loc.Get("Common.Exit"),
            DialogResult = DialogResult.Cancel,
            Location = new System.Drawing.Point(388, 300),
            Width = 68,
        };
        _createButton.Click += (_, _) => TryCreate();
        AcceptButton = _createButton;
        CancelButton = exitButton;

        var tooltip = new ToolTip { ShowAlways = true };
        tooltip.SetToolTip(_pinBox, _loc.Get("Ui.FirstRunWizardForm.Tooltip.PinRecommended"));

        Controls.Add(welcome);
        Controls.Add(explanation);
        Controls.Add(nameLabel);
        Controls.Add(_nameBox);
        Controls.Add(pinLabel);
        Controls.Add(_pinBox);
        Controls.Add(_pinConfirmBox);
        Controls.Add(_statusLabel);
        Controls.Add(_createButton);
        Controls.Add(exitButton);

        Shown += (_, _) => _nameBox.Focus();
    }

    // The profile that the wizard created. null when the user
    // dismissed the wizard through the Exit button — the caller
    // must then close the app (§12.3 makes the wizard mandatory).
    public Profile? CreatedProfile { get; private set; }

    private void TryCreate()
    {
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _statusLabel.Text = _loc.Get("Ui.FirstRunWizardForm.NameRequired");
            _nameBox.Focus();
            return;
        }

        var pin = _pinBox.Text;
        var pinConfirm = _pinConfirmBox.Text;
        if (!string.IsNullOrEmpty(pin) || !string.IsNullOrEmpty(pinConfirm))
        {
            if (!string.Equals(pin, pinConfirm, StringComparison.Ordinal))
            {
                _statusLabel.Text = _loc.Get("Ui.FirstRunWizardForm.PinMismatch");
                _pinConfirmBox.Focus();
                return;
            }
        }

        try
        {
            // Registry Create() forces Admin on the first profile
            // regardless of the requested role, but pass Admin
            // explicitly to keep the intent obvious.
            var created = _registry.Create(name, ProfileRole.Admin);
            if (!string.IsNullOrEmpty(pin))
            {
                _registry.SetPin(created.Id, pin);
                // Refresh HasPin on the returned record.
                created = _registry.GetById(created.Id) ?? created;
            }
            CreatedProfile = created;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
        }
    }
}
