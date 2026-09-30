using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using QRCoder;

namespace MedReminder.UI.Forms;

// Shows a pairing offer (B.1 Phase 4c, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §6.1): the QR code for a phone, and the same
// code as text for another PC. The code is a secret while the offer
// lasts: it stays hidden until the user asks to show it, and the dialog
// says who can use it. It closes itself when the offer expires; the caller
// ends the offer (deletes the pairing file) after it closes.
//
// The window used to be excluded from screen capture
// (SetWindowDisplayAffinity). That hid it entirely in remote-control
// sessions (Remote Desktop, RustDesk, AnyDesk…), where the application
// looked frozen behind an invisible modal window, and it is no barrier
// against a program running as the user, which can read the protected
// keys directly. Showing the code on request keeps it out of screenshots,
// recordings and shared screens taken before the user reveals it
// (product owner, 2026-09-30).
internal sealed class SyncPairingDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly DateTimeOffset _expiresAt;
    private readonly TimeProvider _clock;
    private readonly Label _remaining;
    private readonly System.Windows.Forms.Timer _timer;

    public SyncPairingDialog(ILocalizationService localization, SyncPairingOffer offer, TimeProvider clock)
        : this(localization, offer.Code.Text, offer.ExpiresAt, clock, "Ui.SyncDialog.Pair.Hint")
    {
    }

    // Household step H3d: an installation code (mrpair2) and its hint.
    public SyncPairingDialog(ILocalizationService localization, string codeText, DateTimeOffset expiresAt,
        TimeProvider clock, string hintKey)
    {
        _loc = localization;
        _expiresAt = expiresAt;
        _clock = clock;

        Text = _loc.Get("Ui.SyncDialog.Pair.Title");
        Width = 560;
        Height = 720;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        layout.Controls.Add(new Label
        {
            Text = _loc.Get(hintKey),
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            Margin = new Padding(0, 0, 0, 8),
        });

        // Hidden until "Show the code": nothing secret is on screen before.
        var reveal = new Button
        {
            Text = _loc.Get("Ui.SyncDialog.Pair.Reveal"),
            AutoSize = true,
            Height = 32,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 8, 0, 8),
        };
        layout.Controls.Add(reveal);

        var qr = new PictureBox
        {
            Image = RenderQr(codeText),
            SizeMode = PictureBoxSizeMode.Zoom,
            Width = 320,
            Height = 320,
            Anchor = AnchorStyles.None,
            BackColor = Color.White,
            Visible = false,
        };
        layout.Controls.Add(qr);

        var codeLabel = new Label
        {
            Text = _loc.Get("Ui.SyncDialog.Pair.CodeLabel"),
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
            Visible = false,
        };
        layout.Controls.Add(codeLabel);
        var codeBox = new TextBox
        {
            Text = codeText,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            Height = 48,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9F),
            Visible = false,
        };
        layout.Controls.Add(codeBox);
        reveal.Click += (_, _) =>
        {
            reveal.Visible = false;
            qr.Visible = true;
            codeLabel.Visible = true;
            codeBox.Visible = true;
        };

        _remaining = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        layout.Controls.Add(_remaining);
        layout.Controls.Add(new Label
        {
            Text = _loc.Get("Ui.SyncDialog.Pair.Secret") + " " + _loc.Get("Ui.SyncDialog.Pair.RevealHint"),
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

    private void Tick()
    {
        var left = _expiresAt - _clock.GetUtcNow();
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
}
