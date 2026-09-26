using System.Drawing.Drawing2D;

namespace MedReminder.UI.Controls;

// One legend item of the therapy timeline: the symbol drawn by
// TimelinePainter followed by its label. Sizes follow the font, so the
// legend grows with Windows scaling.
internal sealed class TimelineLegendEntry : Control
{
    public enum Symbol
    {
        Active,
        Suspended,
        ScheduleChange,
        TaperStage,
        RunOut,
        Today,
    }

    private readonly Symbol _symbol;

    public TimelineLegendEntry(Symbol symbol, string text)
    {
        _symbol = symbol;
        Text = text;
        AccessibleName = text;
        AccessibleRole = AccessibleRole.StaticText;
        TabStop = false;
        AutoSize = true;
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor,
            true);
        SetStyle(ControlStyles.Selectable, false);
    }

    private int SymbolWidth => Font.Height * 2;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix);
        return new Size(
            SymbolWidth + Font.Height / 2 + text.Width,
            Math.Max(text.Height, Font.Height) + Font.Height / 4);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = Text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var p = TimelinePainter.Current;
        var stroke = TimelinePainter.StrokeWidth(Font);

        var lh = Font.Height;
        var midY = ClientSize.Height / 2f;
        var symbol = new RectangleF(stroke, midY - lh * 0.3f, SymbolWidth - stroke * 2, lh * 0.6f);
        var center = new PointF(symbol.X + symbol.Width / 2f, midY);

        switch (_symbol)
        {
            case Symbol.Active:
                TimelinePainter.DrawActive(g, symbol, p, stroke);
                break;
            case Symbol.Suspended:
                TimelinePainter.DrawSuspended(g, symbol, p, stroke);
                break;
            case Symbol.ScheduleChange:
                TimelinePainter.DrawDiamond(g, center, lh * 0.8f, p, filled: true, stroke);
                break;
            case Symbol.TaperStage:
                TimelinePainter.DrawDiamond(g, center, lh * 0.8f, p, filled: false, stroke);
                break;
            case Symbol.RunOut:
                TimelinePainter.DrawRunOut(g,
                    new PointF(center.X, midY - lh * 0.05f), lh * 0.6f, midY + lh * 0.45f, p, stroke);
                break;
            case Symbol.Today:
                TimelinePainter.DrawTodayLine(g, center.X, midY - lh * 0.5f, midY + lh * 0.5f, p, stroke);
                break;
        }

        var textBounds = new Rectangle(
            SymbolWidth + lh / 2, 0,
            ClientSize.Width - SymbolWidth - lh / 2, ClientSize.Height);
        TextRenderer.DrawText(g, Text, Font, textBounds, ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left
            | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}
