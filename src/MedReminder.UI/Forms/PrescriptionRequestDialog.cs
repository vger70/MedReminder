using System.ComponentModel;
using System.Diagnostics;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Application.Prescriptions;

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

    private bool _sending;

    public PrescriptionRequestDialog(
        EmailMessage draft,
        string doctorAddress,
        bool smtpConfigured,
        Func<string, string, string, CancellationToken, Task> sendAsync,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(sendAsync);
        _loc = localization;
        _doctorAddress = doctorAddress?.Trim() ?? string.Empty;
        _canSend = smtpConfigured && _doctorAddress.Length > 0;
        _sendAsync = sendAsync;

        Text = _loc.Get("Ui.PrescriptionRequestDialog.Title");
        Width = 640;
        Height = 560;
        MinimumSize = new Size(520, 420);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9.75F);

        var recipientValue = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Text = _doctorAddress.Length > 0
                ? _doctorAddress
                : _loc.Get("Ui.PrescriptionRequestDialog.Recipient.None"),
            ForeColor = _doctorAddress.Length > 0 ? SystemColors.ControlText : Color.DimGray,
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
            MaximumSize = new Size(580, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(3, 6, 3, 3),
            Text = _loc.Get(_canSend
                ? "Ui.PrescriptionRequestDialog.Hint"
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

        _copyButton = new Button { Text = _loc.Get("Ui.PrescriptionRequestDialog.Copy"), AutoSize = true, Height = 32 };
        _mailClientButton = new Button { Text = _loc.Get("Ui.PrescriptionRequestDialog.OpenMailClient"), AutoSize = true, Height = 32 };
        _sendButton = new Button { Text = _loc.Get("Ui.PrescriptionRequestDialog.Send"), AutoSize = true, Height = 32, Enabled = _canSend };
        _closeButton = new Button { Text = _loc.Get("Common.Close"), DialogResult = DialogResult.Cancel, AutoSize = true, Height = 32 };

        _copyButton.Click += (_, _) => CopyDraft(showConfirmation: true);
        _mailClientButton.Click += (_, _) => OpenInMailClient();
        _sendButton.Click += async (_, _) => await SendAsync();

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.Add(_closeButton);
        buttonPanel.Controls.Add(_sendButton);
        buttonPanel.Controls.Add(_mailClientButton);
        buttonPanel.Controls.Add(_copyButton);

        Controls.Add(table);
        Controls.Add(buttonPanel);
        CancelButton = _closeButton;

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
        MessageBox.Show(this,
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
                MessageBox.Show(this,
                    _loc.Get("Ui.PrescriptionRequestDialog.Copied"),
                    _loc.Get("Common.Information"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
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
                MessageBox.Show(this,
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
                MessageBox.Show(this,
                    _loc.Get("Ui.PrescriptionRequestDialog.MailClientUnavailable"),
                    _loc.Get("Common.Information"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    private async Task SendAsync()
    {
        if (!_canSend || _sending || !HasDraft()) return;

        var confirm = MessageBox.Show(this,
            _loc.Get("Ui.PrescriptionRequestDialog.ConfirmSend", _doctorAddress),
            _loc.Get("Common.Confirm"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        SetSending(true);
        try
        {
            using var cts = new CancellationTokenSource(SendTimeout);
            await _sendAsync(_doctorAddress, _subjectBox.Text.Trim(), _bodyBox.Text, cts.Token);
            SetSending(false);
            MessageBox.Show(this,
                _loc.Get("Ui.PrescriptionRequestDialog.Sent"),
                _loc.Get("Common.Information"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            SetSending(false);
            MessageBox.Show(this,
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
