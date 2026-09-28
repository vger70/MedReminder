using System.Globalization;
using System.Runtime.InteropServices;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using QRCoder;

namespace MedReminder.UI.Forms;

// Shows a pairing offer (B.1 Phase 4c, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §6.1): the QR code for a phone, and the same
// code as text for another PC. The code is a secret while the offer
// lasts: the dialog says so, and its window is excluded from screen
// capture (SetWindowDisplayAffinity). It closes itself when the offer
// expires; the caller ends the offer (deletes the pairing file) after it
// closes.
internal sealed class SyncPairingDialog : MedReminderFormBase
{
    // Windows 10 2004+: the window is left out of captures and shown
    // black in them. Older versions only support WDA_MONITOR.
    private const uint WdaExcludeFromCapture = 0x11;
    private const uint WdaMonitor = 0x01;

    private readonly ILocalizationService _loc;
    private readonly SyncPairingOffer _offer;
    private readonly TimeProvider _clock;
    private readonly Label _remaining;
    private readonly System.Windows.Forms.Timer _timer;

    public SyncPairingDialog(ILocalizationService localization, SyncPairingOffer offer, TimeProvider clock)
    {
        _loc = localization;
        _offer = offer;
        _clock = clock;

        Text = _loc.Get("Ui.SyncDialog.Pair.Title");
        Width = 560;
        Height = 720;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.75F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        layout.Controls.Add(new Label
        {
            Text = _loc.Get("Ui.SyncDialog.Pair.Hint"),
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            Margin = new Padding(0, 0, 0, 8),
        });

        var qr = new PictureBox
        {
            Image = RenderQr(offer.Code.Text),
            SizeMode = PictureBoxSizeMode.Zoom,
            Width = 320,
            Height = 320,
            Anchor = AnchorStyles.None,
            BackColor = Color.White,
        };
        layout.Controls.Add(qr);

        layout.Controls.Add(new Label
        {
            Text = _loc.Get("Ui.SyncDialog.Pair.CodeLabel"),
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
        });
        layout.Controls.Add(new TextBox
        {
            Text = offer.Code.Text,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            Height = 48,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9F),
        });

        _remaining = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        layout.Controls.Add(_remaining);
        layout.Controls.Add(new Label
        {
            Text = _loc.Get("Ui.SyncDialog.Pair.Secret"),
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 8, 0, 0),
        });

        var close = new Button { Text = _loc.Get("Common.Close"), DialogResult = DialogResult.OK, AutoSize = true, Height = 32 };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttons.Controls.Add(close);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Tick();
        Shown += (_, _) =>
        {
            Tick();
            _timer.Start();
        };
        FormClosed += (_, _) =>
        {
            _timer.Dispose();
            qr.Image?.Dispose();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture)) SetWindowDisplayAffinity(Handle, WdaMonitor);
    }

    private void Tick()
    {
        var left = _offer.ExpiresAt - _clock.GetUtcNow();
        if (left <= TimeSpan.Zero)
        {
            _timer.Stop();
            DialogResult = DialogResult.Cancel;
            return;
        }
        _remaining.Text = _loc.Get("Ui.SyncDialog.Pair.Remaining",
            left.ToString(@"m\:ss", CultureInfo.CurrentCulture));
    }

    private static Bitmap RenderQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        using var stream = new MemoryStream(png.GetGraphic(8));
        // A copy: Image.FromStream needs its stream for the image's life.
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    // DllImport rather than LibraryImport: the project does not allow
    // unsafe code, which the LibraryImport generator needs.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
#pragma warning restore SYSLIB1054
}
