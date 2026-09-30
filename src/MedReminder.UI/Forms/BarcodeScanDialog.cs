using System.Diagnostics;
using System.Drawing.Imaging;
using System.Media;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Storage;
using MedReminder.UI.Controls;
using MedReminder.UI.UiExtensions;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Forms;

// Modal dialog that reads one medicine barcode and returns it parsed.
// Two sources feed the same parser: a USB HID scanner (keyboard wedge)
// or a code typed by hand, which is the default, and the webcam, which
// starts only when the user asks for it and stops as soon as the user
// switches back, a code is read, the timeout fires or the dialog
// closes. See docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §4.2, §5A, §5B.
//
// No AcceptButton on purpose: the scanner's Enter suffix must end the
// payload, never press a default button.
internal sealed class BarcodeScanDialog : MedReminderFormBase
{
    // Minimum keystrokes for an unterminated payload to be submitted
    // on idle: the shortest recognized shape is the 6-character
    // Code 32 form.
    private const int MinimumIdleKeystrokes = ItalianPharmacode.Code32Length;

    // Client size of the dialog in webcam mode, at 96 DPI and normal
    // text size; scaled like the rest of the layout.
    private static readonly Size WebcamSize = new(640, 580);

    public BarcodeContent? Result { get; private set; }

    private readonly ILocalizationService _loc;
    private readonly IBarcodeParser _parser;
    private readonly ICameraCaptureService _camera;
    private readonly BarcodeCaptureOptions _options;
    private readonly ILogger _log;
    private readonly ScannerInputBox _input;
    private readonly Label _status;
    private readonly TableLayoutPanel _scannerPanel;
    private readonly TableLayoutPanel _webcamPanel;
    private readonly PictureBox _preview;
    private readonly Button _tryAgainButton;
    private readonly Button _webcamActionButton;
    private readonly System.Windows.Forms.Timer _idleTimer;
    private readonly ScanBurstDetector _burst = new();

    private CancellationTokenSource? _scanCts;
    private Bitmap? _previewBitmap;
    private (BarcodeContent Content, RawBarcode Raw)? _webcamRead;
    private CameraAvailability _failure;

    public BarcodeScanDialog(
        ILocalizationService localization,
        IBarcodeParser parser,
        ICameraCaptureService camera,
        BarcodeCaptureOptions options,
        ILogger log)
    {
        _loc = localization;
        _parser = parser;
        _camera = camera;
        _options = options;
        _log = log;

        Text = _loc.Get("Ui.BarcodeScanDialog.Title");
        ClientSize = new Size(520, 280);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var prompt = NewWrappingLabel(_loc.Get("Ui.BarcodeScanDialog.Prompt"));
        prompt.Font = new Font(Font, FontStyle.Bold);
        prompt.Margin = new Padding(4, 4, 4, 8);

        // Scanner panel: the default mode, holds no resource.
        var hint = NewWrappingLabel(_loc.Get("Ui.BarcodeScanDialog.ScannerHint"));
        hint.ForeColor = UiColors.Hint;
        _input = new ScannerInputBox { Dock = DockStyle.Fill, Margin = new Padding(4, 0, 4, 8) };
        var useWebcamButton = NewButton(_loc.Get("Ui.BarcodeScanDialog.UseWebcam"));
        useWebcamButton.Click += (_, _) => StartWebcam();
        _scannerPanel = NewStack(hint, _input, useWebcamButton);
        _scannerPanel.AutoSize = true;
        _scannerPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;

        // Webcam panel: hidden until the user asks for the camera.
        var webcamHint = NewWrappingLabel(_loc.Get("Ui.BarcodeScanDialog.WebcamHint"));
        webcamHint.ForeColor = UiColors.Hint;
        _preview = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = SystemColors.ControlDark,
            Margin = new Padding(4, 0, 4, 8),
        };
        _preview.Paint += OnPreviewPaint;
        var useScannerButton = NewButton(_loc.Get("Ui.BarcodeScanDialog.UseScanner"));
        useScannerButton.Click += (_, _) => ShowScanner();
        _tryAgainButton = NewButton(_loc.Get("Ui.BarcodeScanDialog.TryAgain"));
        _tryAgainButton.Click += (_, _) => StartWebcam();
        _webcamActionButton = NewButton(string.Empty);
        _webcamActionButton.Click += OnWebcamActionClick;
        var webcamButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        webcamButtons.Controls.AddRange([useScannerButton, _tryAgainButton, _webcamActionButton]);
        _webcamPanel = NewStack(webcamHint, _preview, webcamButtons);
        _webcamPanel.RowStyles[1] = new RowStyle(SizeType.Percent, 100);
        _webcamPanel.Dock = DockStyle.Fill;
        _webcamPanel.Visible = false;

        _status = NewWrappingLabel(_loc.Get("Ui.BarcodeScanDialog.Waiting"));
        _status.Margin = new Padding(4, 0, 4, 4);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(prompt, 0, 0);
        layout.Controls.Add(_scannerPanel, 0, 1);
        layout.Controls.Add(_webcamPanel, 0, 2);
        layout.Controls.Add(_status, 0, 3);

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

    // Closing by any path (Cancel, Esc, title-bar close, a read)
    // releases the camera.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopWebcam();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _idleTimer.Dispose();
            _scanCts?.Dispose();
            _preview.Image = null;
            _previewBitmap?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ------- Scanner (keyboard wedge) -------

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
                "Barcode read: scanner, length {Length}, national code {HasNationalCode}, GTIN {HasGtin}, idle {OnIdle}.",
                payload.Length, content.NationalCode is not null, content.Gtin is not null, onIdle);
            Accept(content);
            return;
        }

        // An idle check on an unrecognized burst stays silent: the
        // scanner may still be typing, or the user may add a suffix.
        if (onIdle) return;

        _log.LogWarning("Barcode not recognized: scanner, length {Length}.", payload.Length);
        _status.Text = _loc.Get("Ui.BarcodeScanDialog.Unrecognized");
        _input.Clear();
        _input.Focus();
    }

    private void Accept(BarcodeContent content)
    {
        Result = content;
        SystemSounds.Asterisk.Play();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowScanner()
    {
        StopWebcam();
        _webcamPanel.Visible = false;
        _scannerPanel.Visible = true;
        _status.Text = _loc.Get("Ui.BarcodeScanDialog.Waiting");
        _input.Focus();
    }

    // ------- Webcam -------

    private async void StartWebcam()
    {
        StopWebcam();
        _scannerPanel.Visible = false;
        _webcamPanel.Visible = true;
        _tryAgainButton.Visible = false;
        _webcamActionButton.Visible = false;
        _status.Text = _loc.Get("Ui.BarcodeScanDialog.WebcamStarting");
        EnsureWebcamSize();

        var scanCts = new CancellationTokenSource();
        _scanCts = scanCts;
        scanCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.ScanTimeoutSeconds)));
        _webcamRead = null;
        var started = Stopwatch.StartNew();

        CameraScanResult result;
        try
        {
            result = await _camera.ScanAsync(AcceptFromWebcam, OnFrame, scanCts.Token);
        }
        catch (Exception ex)
        {
            // async void handler: never let the exception escape. The
            // service reports camera faults as a result, so this is a bug.
            _log.LogError(ex, "Webcam scan failed unexpectedly.");
            result = new CameraScanResult(CameraAvailability.InitializationFailed, null);
        }

        // The user switched back, started again or closed the dialog:
        // a newer state owns the UI.
        var superseded = !ReferenceEquals(_scanCts, scanCts) || IsDisposed || Disposing;
        var timedOut = !superseded && scanCts.IsCancellationRequested
            && result.Availability == CameraAvailability.Available;
        if (ReferenceEquals(_scanCts, scanCts)) _scanCts = null;
        scanCts.Dispose();
        if (superseded) return;

        var read = _webcamRead;
        if (result.Barcode is { } raw && read is not null)
        {
            _log.LogInformation(
                "Barcode read: webcam, symbology {Symbology}, length {Length}, national code {HasNationalCode}, GTIN {HasGtin}, {Seconds:0.0} s.",
                raw.Symbology, raw.Payload.Length, read.Value.Content.NationalCode is not null,
                read.Value.Content.Gtin is not null, started.Elapsed.TotalSeconds);
            Accept(read.Value.Content);
            return;
        }

        ClearPreview();
        _tryAgainButton.Visible = true;
        if (timedOut)
        {
            _log.LogWarning("Webcam scan: no barcode detected within {Seconds} s.", _options.ScanTimeoutSeconds);
            _status.Text = _loc.Get("Ui.BarcodeScanDialog.NoBarcodeDetected");
            return;
        }
        ShowFailure(result.Availability);
    }

    // Runs on the camera worker thread. The parser is pure and
    // thread-safe; a code it rejects keeps the camera scanning.
    private bool AcceptFromWebcam(RawBarcode raw)
    {
        var content = _parser.Parse(raw);
        if (!content.HasLookupKey) return false;
        _webcamRead = (content, raw);
        return true;
    }

    private void StopWebcam()
    {
        var cts = _scanCts;
        _scanCts = null;
        cts?.Cancel();
    }

    private void ShowFailure(CameraAvailability availability)
    {
        _failure = availability;
        (string status, string? action) = availability switch
        {
            CameraAvailability.NoDeviceFound => ("Ui.BarcodeScanDialog.NoDevice", null),
            CameraAvailability.PermissionDenied => ("Ui.BarcodeScanDialog.PermissionDenied", "Ui.BarcodeScanDialog.OpenPrivacySettings"),
            _ => ("Ui.BarcodeScanDialog.InitializationFailed", "Ui.BarcodeScanDialog.CopyLogLocation"),
        };
        _status.Text = _loc.Get(status);
        _webcamActionButton.Visible = action is not null;
        if (action is not null) _webcamActionButton.Text = _loc.Get(action);
    }

    private void OnWebcamActionClick(object? sender, EventArgs e)
    {
        try
        {
            if (_failure == CameraAvailability.PermissionDenied)
            {
                // Settings → Privacy & security → Camera (§7.2).
                Process.Start(new ProcessStartInfo("ms-settings:privacy-webcam") { UseShellExecute = true });
            }
            else
            {
                Clipboard.SetText(AppDataPaths.GetLogsDirectory());
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Webcam scan: the {Availability} action failed.", _failure);
        }
    }

    // Called on the UI thread; the pixels are valid only during the
    // call, so they are copied into a bitmap reused across frames.
    private void OnFrame(CameraFrameStatus status)
    {
        if (IsDisposed || !_webcamPanel.Visible) return;
        var frame = status.Preview;
        if (_previewBitmap is null || _previewBitmap.Width != frame.Width || _previewBitmap.Height != frame.Height)
        {
            _preview.Image = null;
            _previewBitmap?.Dispose();
            _previewBitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppRgb);
        }

        var rect = new Rectangle(0, 0, frame.Width, frame.Height);
        var data = _previewBitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            var rowBytes = frame.Width * 4;
            for (var y = 0; y < frame.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    frame.Bgra32, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
        }
        finally
        {
            _previewBitmap.UnlockBits(data);
        }

        if (_preview.Image != _previewBitmap) _preview.Image = _previewBitmap;
        else _preview.Invalidate();
        if (status.FramesDecoded > 0 && _scanCts is not null)
        {
            _status.Text = _loc.Get("Ui.BarcodeScanDialog.WebcamScanning");
        }
    }

    private void ClearPreview()
    {
        _preview.Image = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
    }

    // A frame in the middle of the preview, where the decoder reads
    // best; purely a guide for the user.
    private void OnPreviewPaint(object? sender, PaintEventArgs e)
    {
        if (_preview.Image is null) return;
        var area = _preview.ClientRectangle;
        var width = area.Width * 7 / 10;
        var height = area.Height * 45 / 100;
        var guide = new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
        using var pen = new Pen(Color.FromArgb(200, Color.White), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        e.Graphics.DrawRectangle(pen, guide);
    }

    // Grows the dialog for the preview once, scaled like the layout
    // (display DPI and text size), and never beyond the screen.
    private void EnsureWebcamSize()
    {
        var factor = Math.Max(1f, DeviceDpi / 96f) * Math.Max(1f, TextScale);
        var wanted = new Size((int)(WebcamSize.Width * factor), (int)(WebcamSize.Height * factor));
        var area = Screen.FromControl(this).WorkingArea;
        var width = Math.Min(Math.Max(ClientSize.Width, wanted.Width), area.Width);
        var height = Math.Min(Math.Max(ClientSize.Height, wanted.Height), area.Height);
        if (width == ClientSize.Width && height == ClientSize.Height) return;

        var center = new Point(Left + Width / 2, Top + Height / 2);
        ClientSize = new Size(width, height);
        Location = new Point(
            Math.Clamp(center.X - Width / 2, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(center.Y - Height / 2, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    // ------- Layout helpers -------

    private static Label NewWrappingLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Fill,
        Margin = new Padding(4, 0, 4, 8),
    };

    private static Button NewButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(120, 32),
        Margin = new Padding(4, 0, 4, 4),
    };

    private static TableLayoutPanel NewStack(params Control[] controls)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Margin = new Padding(0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var control in controls)
        {
            panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(control, 0, panel.RowCount - 1);
        }
        return panel;
    }
}
