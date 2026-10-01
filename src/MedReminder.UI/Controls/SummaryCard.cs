using System.ComponentModel;
using MedReminder.Application.Overview;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Controls;

// Summary card above the main grid (docs/analysis/ANALYSIS-UI-MODERNIZATION.md
// §5.1, F4): a count of medicines in one status group and its caption.
// Clicking the card (or Enter/Space when focused) filters the grid to
// that group; the card of the active filter is drawn pressed.
internal sealed class SummaryCard : Control
{
    private const int CornerRadius = 6;

    private int _count;
    private bool _pressed;
    private bool _hover;
    private Font? _countFont;

    public SummaryCard(MedicineListBucket bucket, string caption)
    {
        Bucket = bucket;
        Text = caption;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        Height = 76;
        Margin = new Padding(0, 0, UiTheme.Space.M, 0);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable
            | ControlStyles.SupportsTransparentBackColor, true);
        UpdateAccessibleName();
    }

    public MedicineListBucket Bucket { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Count
    {
        get => _count;
        set { _count = value; UpdateAccessibleName(); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Pressed
    {
        get => _pressed;
        set { _pressed = value; UpdateAccessibleName(); Invalidate(); }
    }

    private void UpdateAccessibleName()
        => AccessibleName = $"{Text}: {_count}";

    // Screen readers announce the active filter as a pressed button.
    protected override AccessibleObject CreateAccessibilityInstance() => new CardAccessibleObject(this);

    private sealed class CardAccessibleObject(SummaryCard owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State
            => owner.Pressed ? base.State | AccessibleStates.Pressed : base.State;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _countFont?.Dispose();
        _countFont = null;
        base.OnFontChanged(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = UiTheme.Palette;
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var clear = new SolidBrush(Parent?.BackColor ?? p.Background))
        {
            g.FillRectangle(clear, ClientRectangle);
        }

        var borderWidth = _pressed ? 2 : 1;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = RoundedRect(bounds, CornerRadius))
        using (var fill = new SolidBrush(_hover ? p.Hover : p.Surface))
        using (var border = new Pen(_pressed ? p.Accent : p.Border, borderWidth))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        var pad = Height / 6;
        _countFont ??= new Font(UiTheme.Fonts.DisplayFamily, Font.Size * 1.9f, FontStyle.Regular);
        var countHeight = TextRenderer.MeasureText(g, "0", _countFont).Height;
        TextRenderer.DrawText(g, _count.ToString(System.Globalization.CultureInfo.CurrentCulture), _countFont,
            new Rectangle(pad, pad / 2, Width - 2 * pad, countHeight), CountColor(p),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(pad, pad / 2 + countHeight, Width - 2 * pad, Height - countHeight - pad / 2),
            p.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4), p.Text, p.Surface);
        }
    }

    // Status colour for a group that has medicines in it; a zero count
    // stays neutral so only what needs attention stands out.
    private Color CountColor(UiPalette p)
    {
        if (_count == 0) return p.TextSecondary;
        return Bucket switch
        {
            MedicineListBucket.Empty => p.DangerText,
            MedicineListBucket.Warning => p.WarningText,
            MedicineListBucket.Suspended => p.NeutralText,
            _ => p.Text,
        };
    }

    internal static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = Math.Max(1, radius * 2);
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _countFont?.Dispose();
        base.Dispose(disposing);
    }
}
