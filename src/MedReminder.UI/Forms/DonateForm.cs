using System.Drawing;
using System.Windows.Forms;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Donations;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Forms;

// "Support Development" dialog (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §8). The user picks a fixed
// donation tier (€2/€5/€10/€20) or a provider-native custom amount and
// a provider (Stripe / PayPal); "Continue" hands off to DonationService,
// which opens the provider's public hosted payment page in the default
// browser.
//
// The dialog binds only to DonationService: it holds NO URL literals
// and NO provider-specific branching beyond selecting the
// DonationProvider enum value. Only enabled providers are shown. The
// custom option appears only when the selected provider has a valid
// "choose your amount" link. There is NO free numeric field that gets
// turned into a URL — the amount is chosen on the provider's page.
internal sealed class DonateForm : MedReminderFormBase
{
    private readonly DonationService _donations;
    private readonly ILocalizationService _loc;
    private readonly ILogger<DonateForm> _log;

    private readonly List<(RadioButton radio, decimal amount)> _amountRadios = new();
    private RadioButton? _customRadio;
    private Label? _customHelp;
    private readonly List<RadioButton> _providerRadios = new();
    private Button _continueButton = null!;

    public DonateForm(
        DonationService donations,
        ILocalizationService localization,
        ILogger<DonateForm> log)
    {
        _donations = donations;
        _loc = localization;
        _log = log;

        Text = _loc.Get("Ui.Donate.Title");
        Width = 460;
        Height = 420;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9.75F);

        BuildLayout();
        UpdateCustomOptionAvailability();
        UpdateContinueButton();
    }

    private void BuildLayout()
    {
        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(408, 0),
            Text = _loc.Get("Ui.Donate.Intro"),
        };

        var amountLabel = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = _loc.Get("Ui.Donate.AmountLabel"),
            Margin = new Padding(0, 12, 0, 4),
        };

        // Fixed tiers. Each tier is its own pre-made fixed-amount link;
        // the number never reaches the URL.
        var amountsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0),
        };
        foreach (var amount in new[] { 2m, 5m, 10m, 20m })
        {
            var radio = new RadioButton
            {
                AutoSize = true,
                Text = "€" + amount.ToString("0", _loc.CurrentCulture),
                Margin = new Padding(0, 0, 12, 0),
            };
            _amountRadios.Add((radio, amount));
            amountsPanel.Controls.Add(radio);
        }
        _amountRadios[0].radio.Checked = true;

        // Custom amount — a radio like the tiers. Selecting it opens the
        // provider-native "choose your amount" link; the amount is
        // entered on the provider's page, never here.
        _customRadio = new RadioButton
        {
            AutoSize = true,
            Text = _loc.Get("Ui.Donate.CustomAmount"),
            Margin = new Padding(0, 4, 0, 0),
        };
        _customRadio.CheckedChanged += (_, _) => UpdateCustomHelpVisibility();
        _customHelp = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(408, 0),
            ForeColor = Color.DimGray,
            Text = _loc.Get("Ui.Donate.CustomAmount.Help"),
            Margin = new Padding(18, 0, 0, 0),
            Visible = false,
        };

        var paymentLabel = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = _loc.Get("Ui.Donate.PaymentLabel"),
            Margin = new Padding(0, 12, 0, 4),
        };

        var providersPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0),
        };
        foreach (var provider in new[] { DonationProvider.Stripe, DonationProvider.PayPal })
        {
            if (!_donations.IsProviderUsable(provider)) continue;
            var radio = new RadioButton
            {
                AutoSize = true,
                Text = _donations.GetProviderName(provider),
                Tag = provider,
                Margin = new Padding(0, 0, 16, 0),
            };
            radio.CheckedChanged += (_, _) =>
            {
                if (((RadioButton)radio).Checked)
                {
                    UpdateCustomOptionAvailability();
                    UpdateContinueButton();
                }
            };
            _providerRadios.Add(radio);
            providersPanel.Controls.Add(radio);
        }
        if (_providerRadios.Count > 0)
        {
            _providerRadios[0].Checked = true;
        }

        _continueButton = new Button
        {
            AutoSize = true,
            Height = 30,
            Padding = new Padding(8, 0, 8, 0),
        };
        _continueButton.Click += (_, _) => OnContinue();

        var closeButton = new Button
        {
            Text = _loc.Get("Ui.Donate.Close"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Height = 30,
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0),
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(_continueButton);

        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16, 16, 16, 8),
        };
        body.Controls.Add(intro);
        body.Controls.Add(amountLabel);
        body.Controls.Add(amountsPanel);
        body.Controls.Add(_customRadio);
        body.Controls.Add(_customHelp);
        body.Controls.Add(paymentLabel);
        body.Controls.Add(providersPanel);
        body.Controls.Add(buttons);

        Controls.Add(body);
        CancelButton = closeButton;
        AcceptButton = _continueButton;
    }

    private DonationProvider? SelectedProvider
    {
        get
        {
            foreach (var radio in _providerRadios)
            {
                if (radio.Checked && radio.Tag is DonationProvider provider)
                {
                    return provider;
                }
            }
            return null;
        }
    }

    // Show the custom option only when the selected provider offers a
    // valid "choose your amount" link; otherwise hide it and fall back
    // to a fixed tier (§8.2).
    private void UpdateCustomOptionAvailability()
    {
        if (_customRadio is null) return;
        var provider = SelectedProvider;
        var supported = provider.HasValue && _donations.SupportsCustomAmount(provider.Value);

        _customRadio.Visible = supported;
        if (!supported)
        {
            if (_customRadio.Checked)
            {
                _customRadio.Checked = false;
                if (_amountRadios.Count > 0) _amountRadios[0].radio.Checked = true;
            }
        }
        UpdateCustomHelpVisibility();
    }

    private void UpdateCustomHelpVisibility()
    {
        if (_customHelp is null || _customRadio is null) return;
        _customHelp.Visible = _customRadio.Visible && _customRadio.Checked;
    }

    private void UpdateContinueButton()
    {
        var provider = SelectedProvider;
        if (provider.HasValue)
        {
            _continueButton.Enabled = true;
            _continueButton.Text = _loc.Get(
                "Ui.Donate.Continue", _donations.GetProviderName(provider.Value));
        }
        else
        {
            _continueButton.Enabled = false;
            _continueButton.Text = _loc.Get("Ui.Donate.Continue", string.Empty).Trim();
        }
    }

    private void OnContinue()
    {
        var provider = SelectedProvider;
        if (!provider.HasValue) return;

        var result = _customRadio is { Visible: true, Checked: true }
            ? _donations.DonateCustom(provider.Value)
            : _donations.Donate(provider.Value, SelectedAmount());

        if (result.Success)
        {
            // Never claims the payment completed — only that a page was
            // opened (§8.3).
            MessageBox.Show(this,
                _loc.Get(result.UserMessageKey ?? DonationMessageKeys.Launched),
                _loc.Get("Ui.Donate.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var messageKey = result.UserMessageKey
            ?? DonationMessageKeys.ForFailure(result.Reason ?? DonationFailureReason.LaunchFailed);
        MessageBox.Show(this,
            _loc.Get(messageKey),
            _loc.Get("Ui.Donate.Title"),
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private decimal SelectedAmount()
    {
        foreach (var (radio, amount) in _amountRadios)
        {
            if (radio.Checked) return amount;
        }
        return _amountRadios.Count > 0 ? _amountRadios[0].amount : 0m;
    }
}
