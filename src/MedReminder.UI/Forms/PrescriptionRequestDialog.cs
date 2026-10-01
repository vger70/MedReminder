using System.ComponentModel;
using System.Diagnostics;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Application.Prescriptions;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Prescription request draft for the doctor
// (docs/notes/EVOLUTION-PROPOSALS.md §3.4). Shows the recipient and an
// editable subject and body built by PrescriptionRequestTexts, and
// offers three delivery actions, all explicit:
//   - Copy: subject and body to the clipboard.
//   - Open in mail client: mailto: through the shell; falls back to
//     the clipboard when the URI is too long or no client answers.
//   - Send: through the app's SMTP account (MailKit), enabled only when
//     SMTP is configured and a doctor address is set, after an explicit
//     confirmation.
// "Mark as requested" records a prescription requested today
// (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2), whichever way the request
// went out; it is offered when the caller passes markRequested.
//
// The dialog does not log anything: the draft contains health data and
// personal names (CLAUDE.md §7). The send delegate owns the outcome log.
internal sealed class PrescriptionRequestDialog : MedReminderFormBase
{
    // Upper bound for an interactive send. The retry decorator does not
    // back off for explicit-recipient messages, so this only caps a
    // stalled connection beyond the SMTP timeout.
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(90);

    private readonly ILocalizationService _loc;
    private readonly string _doctorAddress;
    private readonly bool _canSend;
    private readonly Func<string, string, string, CancellationToken, Task> _sendAsync;

    private readonly TextBox _subjectBox;
    private readonly TextBox _bodyBox;
    private readonly Button _copyButton;
    private readonly Button _mailClientButton;
    private readonly Button _sendButton;
    private readonly Button _closeButton;
    private readonly Button _markRequestedButton;

    private bool _sending;

    public PrescriptionRequestDialog(
        EmailMessage draft,
        string doctorAddress,
        bool smtpConfigured,
        Func<string, string, string, CancellationToken, Task> sendAsync,
        ILocalizationService localization,
        bool isMaster = true,
        Func<Task>? markRequested = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(sendAsync);
        _loc = localization;
        _doctorAddress = doctorAddress?.Trim() ?? string.Empty;
        // Household step H4c (C5): only the master device sends email.
        _canSend = smtpConfigured && isMaster && _doctorAddress.Length > 0;
        _sendAsync = sendAsync;

        Text = _loc.Get("Ui.PrescriptionRequestDialog.Title");
        Width = 820;
        Height = 600;
        MinimumSize = new Size(640, 460);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;

        var recipientValue = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Text = _doctorAddress.Length > 0
                ? _doctorAddress
                : _loc.Get("Ui.PrescriptionRequestDialog.Recipient.None"),
            ForeColor = _doctorAddress.Length > 0 ? SystemColors.ControlText : UiColors.Hint,
        };

        _subjectBox = new TextBox { Dock = DockStyle.Fill, Text = draft.Subject };
        _bodyBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text = ToWindowsNewLines(draft.Body),
        };

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = UiColors.Hint,
            Margin = new Padding(3, 6, 3, 3),
            Text = _loc.Get(_canSend
                ? "Ui.PrescriptionRequestDialog.Hint"
                : smtpConfigured && !isMaster
                    ? "Ui.PrescriptionRequestDialog.Hint.NotMaster"
                : smtpConfigured
                    ? "Ui.PrescriptionRequestDialog.Hint.NoDoctorAddress"
                    : "Ui.PrescriptionRequestDialog.Hint.NoSmtp"),
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12, 12, 12, 0),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        table.Controls.Add(BuildFieldLabel(_loc.Get("Ui.PrescriptionRequestDialog.Recipient")), 0, 0);
        table.Controls.Add(recipientValue, 1, 0);
        table.Controls.Add(BuildFieldLabel(_loc.Get("Ui.PrescriptionRequestDialog.Subject")), 0, 1);
        table.Controls.Add(_subjectBox, 1, 1);
        var bodyLabel = BuildFieldLabel(_loc.Get("Ui.PrescriptionRequestDialog.Body"));
        bodyLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        bodyLabel.Margin = new Padding(3, 6, 3, 3);
        table.Controls.Add(bodyLabel, 0, 2);
        table.Controls.Add(_bodyBox, 1, 2);
        table.Controls.Add(hint, 1, 3);

        _copyButton = DialogLayout.Button(_loc.Get("Ui.PrescriptionRequestDialog.Copy"));
        _mailClientButton = DialogLayout.Button(_loc.Get("Ui.PrescriptionRequestDialog.OpenMailClient"));
        _sendButton = DialogLayout.Button(_loc.Get("Ui.PrescriptionRequestDialog.Send"));
        _sendButton.Enabled = _canSend;
        _closeButton = DialogLayout.Button(_loc.Get("Common.Close"), DialogResult.Cancel);

        _markRequestedButton = DialogLayout.Button(_loc.Get("Ui.PrescriptionRequestDialog.MarkRequested"));
        _markRequestedButton.Visible = markRequested is not null;
        _markRequestedButton.Click += async (_, _) =>
        {
            if (markRequested is null) return;
            _markRequestedButton.Enabled = false;
            try
            {
                await markRequested();
                _markRequestedButton.Text = _loc.Get("Ui.PrescriptionRequestDialog.MarkedRequested");
            }
            catch (Exception ex)
            {
                _markRequestedButton.Enabled = true;
                UiMessageBox.Show(this, ex.Message, _loc.Get("Ui.PrescriptionsDialog.Error.Save"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        _copyButton.Click += (_, _) => CopyDraft(showConfirmation: true);
        _mailClientButton.Click += (_, _) => OpenInMailClient();
        _sendButton.Click += async (_, _) => await SendAsync();

        // Send is the primary action, last on the right (§5.3).
        var buttonPanel = DialogLayout.ButtonBar(this, _sendButton, _closeButton, _mailClientButton, _copyButton,
            _markRequestedButton);
        // No default button: Enter in the subject must not send the email.
        AcceptButton = null;

        Controls.Add(table);
        Controls.Add(buttonPanel);

        // The hint wraps to the width of the field column, whatever the
        // window width.
        table.SizeChanged += (_, _) =>
        {
            var widths = table.GetColumnWidths();
            if (widths.Length < 2) return;
            hint.MaximumSize = new Size(Math.Max(200, widths[1] - hint.Margin.Horizontal), 0);
        };
        // Every button stays visible at any text size and display
        // scaling: the window is at least as wide as the button bar.
        DialogLayout.KeepButtonsVisible(this, buttonPanel);

        // Closing while a send is in flight would dispose the controls
        // the continuation writes to; the user waits for the outcome.
        FormClosing += (_, e) =>
        {
            if (_sending) e.Cancel = true;
        };
    }

    private static Label BuildFieldLabel(string text) => new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Text = text,
    };

    private static string ToWindowsNewLines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

    private bool HasDraft()
    {
        if (_subjectBox.Text.Trim().Length > 0 && _bodyBox.Text.Trim().Length > 0)
        {
            return true;
        }
        UiMessageBox.Show(this,
            _loc.Get("Ui.PrescriptionRequestDialog.EmptyDraft"),
            _loc.Get("Common.Warning"),
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private bool CopyDraft(bool showConfirmation)
    {
        if (!HasDraft()) return false;
        try
        {
            Clipboard.SetText(_subjectBox.Text.Trim() + "\r\n\r\n" + _bodyBox.Text);
            if (showConfirmation)
            {
                UiMessageBox.Show(this,
                    _loc.Get("Ui.PrescriptionRequestDialog.Copied"),
                    _loc.Get("Common.Information"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message,
                _loc.Get("Ui.PrescriptionRequestDialog.CopyError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void OpenInMailClient()
    {
        if (!HasDraft()) return;

        if (!MailtoLink.TryBuild(_doctorAddress, _subjectBox.Text.Trim(), _bodyBox.Text, out var uri))
        {
            if (CopyDraft(showConfirmation: false))
            {
                UiMessageBox.Show(this,
                    _loc.Get("Ui.PrescriptionRequestDialog.TooLongForMailClient"),
                    _loc.Get("Common.Information"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri!) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // No mail client registered for mailto:, or the shell
            // refused the hand-off. The draft is not lost.
            if (CopyDraft(showConfirmation: false))
            {
                UiMessageBox.Show(this,
                    _loc.Get("Ui.PrescriptionRequestDialog.MailClientUnavailable"),
                    _loc.Get("Common.Information"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    private async Task SendAsync()
    {
        if (!_canSend || _sending || !HasDraft()) return;

        var confirm = ConfirmDialog.Show(_loc, this,
            _loc.Get("Ui.PrescriptionRequestDialog.ConfirmSend", _doctorAddress),
            _loc.Get("Common.Confirm"),
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        SetSending(true);
        try
        {
            using var cts = new CancellationTokenSource(SendTimeout);
            await _sendAsync(_doctorAddress, _subjectBox.Text.Trim(), _bodyBox.Text, cts.Token);
            SetSending(false);
            UiMessageBox.Show(this,
                _loc.Get("Ui.PrescriptionRequestDialog.Sent"),
                _loc.Get("Common.Information"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            SetSending(false);
            UiMessageBox.Show(this,
                _loc.Get("Ui.PrescriptionRequestDialog.SendError", ex.Message),
                _loc.Get("Common.Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetSending(bool sending)
    {
        _sending = sending;
        UseWaitCursor = sending;
        _subjectBox.ReadOnly = sending;
        _bodyBox.ReadOnly = sending;
        _copyButton.Enabled = !sending;
        _mailClientButton.Enabled = !sending;
        _sendButton.Enabled = !sending && _canSend;
        _closeButton.Enabled = !sending;
    }
}
