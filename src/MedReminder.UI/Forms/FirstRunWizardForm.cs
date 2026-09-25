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
        // Layout panels + AutoSize instead of absolute coordinates:
        // the form grows with the DPI scale and with the localized
        // text length, so labels and buttons are never clipped.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var contentWidth = LogicalToDeviceUnits(440);

        var welcome = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(contentWidth, 0),
            Font = new System.Drawing.Font("Segoe UI", 10.5F, System.Drawing.FontStyle.Bold),
            Text = _loc.Get("Ui.FirstRunWizardForm.Welcome"),
            Margin = new Padding(3, 3, 3, 8),
        };

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(contentWidth, 0),
            Text = _loc.Get("Ui.FirstRunWizardForm.Explanation"),
            Margin = new Padding(3, 3, 3, 12),
        };

        var nameLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.FirstRunWizardForm.Name"),
        };
        _nameBox = new TextBox
        {
            Width = contentWidth,
            MaxLength = 100,
            Margin = new Padding(3, 3, 3, 12),
        };

        var pinLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(contentWidth, 0),
            Text = _loc.Get("Ui.FirstRunWizardForm.PinOptional"),
        };
        var pinBoxWidth = (contentWidth - LogicalToDeviceUnits(6)) / 2;
        _pinBox = new TextBox
        {
            Width = pinBoxWidth,
            UseSystemPasswordChar = true,
            MaxLength = 32,
            PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinPlaceholder"),
            Margin = new Padding(0, 0, 3, 0),
        };
        _pinConfirmBox = new TextBox
        {
            Width = pinBoxWidth,
            UseSystemPasswordChar = true,
            MaxLength = 32,
            PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinConfirmPlaceholder"),
            Margin = new Padding(3, 0, 0, 0),
        };
        var pinRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(3, 3, 3, 8),
        };
        pinRow.Controls.Add(_pinBox);
        pinRow.Controls.Add(_pinConfirmBox);

        _statusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(contentWidth, 0),
            ForeColor = System.Drawing.Color.Firebrick,
            Text = string.Empty,
        };

        _createButton = new Button
        {
            Text = _loc.Get("Ui.FirstRunWizardForm.Create"),
            AutoSize = true,
            MinimumSize = new System.Drawing.Size(LogicalToDeviceUnits(110), LogicalToDeviceUnits(34)),
            Padding = new Padding(8, 2, 8, 2),
        };
        var exitButton = new Button
        {
            Text = _loc.Get("Common.Exit"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new System.Drawing.Size(LogicalToDeviceUnits(110), LogicalToDeviceUnits(34)),
            Padding = new Padding(8, 2, 8, 2),
        };
        _createButton.Click += (_, _) => TryCreate();
        AcceptButton = _createButton;
        CancelButton = exitButton;

        // RightToLeft: the first control added sits on the far right,
        // so Exit is added first to keep the [Create] [Exit] order.
        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 12, 3, 3),
        };
        buttonRow.Controls.Add(exitButton);
        buttonRow.Controls.Add(_createButton);

        var tooltip = new ToolTip { ShowAlways = true };
        tooltip.SetToolTip(_pinBox, _loc.Get("Ui.FirstRunWizardForm.Tooltip.PinRecommended"));

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
        };
        foreach (var control in new Control[]
                 { welcome, explanation, nameLabel, _nameBox, pinLabel, pinRow, _statusLabel, buttonRow })
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control);
        }
        Controls.Add(layout);

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
