using System.Media;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using MedReminder.UI.Controls;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Forms;

// Modal dialog that reads one medicine barcode and returns it parsed.
// Phase 1 of A2: input from a USB HID scanner (keyboard wedge) or a
// code typed by hand. The webcam panel is a later phase and plugs in
// here without changing the Result contract.
// See docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §4.2 and §5B.
//
// No AcceptButton on purpose: the scanner's Enter suffix must end the
// payload, never press a default button.
internal sealed class BarcodeScanDialog : MedReminderFormBase
{
    // Minimum keystrokes for an unterminated payload to be submitted
    // on idle: the shortest recognized shape is the 6-character
    // Code 32 form.
    private const int MinimumIdleKeystrokes = ItalianPharmacode.Code32Length;

    public BarcodeContent? Result { get; private set; }

    private readonly ILocalizationService _loc;
    private readonly IBarcodeParser _parser;
    private readonly BarcodeCaptureOptions _options;
    private readonly ILogger _log;
    private readonly ScannerInputBox _input;
    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _idleTimer;
    private readonly ScanBurstDetector _burst = new();

    public BarcodeScanDialog(
        ILocalizationService localization,
        IBarcodeParser parser,
        BarcodeCaptureOptions options,
        ILogger log)
    {
        _loc = localization;
        _parser = parser;
        _options = options;
        _log = log;

        Text = _loc.Get("Ui.BarcodeScanDialog.Title");
        Width = 520;
        Height = 260;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var prompt = new Label
        {
            Text = _loc.Get("Ui.BarcodeScanDialog.Prompt"),
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(470, 0),
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
            Margin = new Padding(4, 4, 4, 8),
        };
        var hint = new Label
        {
            Text = _loc.Get("Ui.BarcodeScanDialog.ScannerHint"),
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(470, 0),
            ForeColor = System.Drawing.Color.DimGray,
            Margin = new Padding(4, 0, 4, 8),
        };
        _input = new ScannerInputBox { Dock = DockStyle.Fill, Margin = new Padding(4, 0, 4, 8) };
        _status = new Label
        {
            Text = _loc.Get("Ui.BarcodeScanDialog.Waiting"),
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(470, 0),
            Margin = new Padding(4, 0, 4, 4),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var control in new Control[] { prompt, hint, _input, _status })
        {
            layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control, 0, layout.RowCount - 1);
        }

        var cancelButton = new Button { Text = _loc.Get("Common.Cancel"), DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        CancelButton = cancelButton;

        _idleTimer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(50, _options.HidIdleCompleteMilliseconds),
        };
        _idleTimer.Tick += OnIdleTick;

        _input.KeyPress += OnInputKeyPress;
        _input.TextChanged += OnInputTextChanged;
        _input.PayloadCompleted += (_, payload) => TrySubmit(payload, onIdle: false);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _input.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _idleTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnInputKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar)) _burst.Record(Environment.TickCount64);
    }

    private void OnInputTextChanged(object? sender, EventArgs e)
    {
        if (_input.TextLength == 0) _burst.Reset();
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    // A scanner configured without a suffix never sends Enter / Tab.
    // Submit on idle only for a fast burst, so a user typing a code by
    // hand is never cut off mid-code.
    private void OnIdleTick(object? sender, EventArgs e)
    {
        _idleTimer.Stop();
        if (_input.TextLength == 0) return;
        if (!_burst.IsBurst(MinimumIdleKeystrokes, _options.HidBurstMaxAverageIntervalMilliseconds)) return;
        TrySubmit(_input.Text, onIdle: true);
    }

    private void TrySubmit(string payload, bool onIdle)
    {
        _idleTimer.Stop();
        var content = _parser.Parse(new RawBarcode(BarcodeSymbology.Unknown, payload));

        if (content.HasLookupKey)
        {
            // Never log the payload: it may carry an FMD serial number.
            _log.LogInformation(
                "Barcode read: length {Length}, national code {HasNationalCode}, GTIN {HasGtin}, idle {OnIdle}.",
                payload.Length, content.NationalCode is not null, content.Gtin is not null, onIdle);
            Result = content;
            SystemSounds.Asterisk.Play();
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        // An idle check on an unrecognized burst stays silent: the
        // scanner may still be typing, or the user may add a suffix.
        if (onIdle) return;

        _log.LogWarning("Barcode not recognized: length {Length}.", payload.Length);
        _status.Text = _loc.Get("Ui.BarcodeScanDialog.Unrecognized");
        _input.Clear();
        _input.Focus();
    }
}
