using System.Drawing.Drawing2D;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Controls;

// Colours and shapes shared by the therapy timeline chart and its
// legend, so the legend always matches what the chart draws.
//
// Every element differs by shape or pattern as well as colour: active
// periods are solid bars, suspensions are hatched bars with a dashed
// border, dosage changes are diamonds (filled for a new schedule,
// hollow for a taper stage), the run-out estimate is a downward
// triangle on a vertical line, today is a dashed vertical line. The
// dark palette follows the app's appearance (UiTheme); with Windows
// high-contrast mode on, system colours replace both palettes.
internal static class TimelinePainter
{
    internal sealed record Palette(
        Color Background,
        Color AlternateRow,
        Color Text,
        Color MutedText,
        Color Grid,
        Color ActiveFill,
        Color ActiveBorder,
        Color SuspendedHatch,
        Color SuspendedBack,
        Color SuspendedBorder,
        Color Marker,
        Color MarkerOutline,
        Color RunOut,
        Color Today,
        Color Selection);

    private static readonly Palette Light = new(
        Background: UiTheme.Light.Surface,
        AlternateRow: Color.FromArgb(246, 248, 250),
        Text: UiTheme.Light.Text,
        MutedText: UiTheme.Light.TextSecondary,
        Grid: Color.FromArgb(222, 226, 230),
        ActiveFill: Color.FromArgb(100, 181, 246),     // #64B5F6
        ActiveBorder: Color.FromArgb(21, 101, 192),    // #1565C0
        SuspendedHatch: Color.FromArgb(97, 97, 97),    // #616161
        SuspendedBack: Color.FromArgb(238, 238, 238),  // #EEEEEE
        SuspendedBorder: Color.FromArgb(66, 66, 66),   // #424242
        Marker: Color.FromArgb(33, 33, 33),            // #212121
        MarkerOutline: Color.White,
        RunOut: Color.FromArgb(198, 40, 40),           // #C62828
        Today: Color.FromArgb(230, 81, 0),             // #E65100
        Selection: UiTheme.Light.Accent);

    // Same shapes on the dark surface: lighter strokes and markers,
    // fills kept darker than the text so labels stay readable.
    private static readonly Palette Dark = new(
        Background: UiTheme.Dark.Surface,
        AlternateRow: Color.FromArgb(56, 56, 56),      // #383838
        Text: UiTheme.Dark.Text,
        MutedText: UiTheme.Dark.TextSecondary,
        Grid: Color.FromArgb(74, 74, 74),              // #4A4A4A
        ActiveFill: Color.FromArgb(46, 110, 184),      // #2E6EB8
        ActiveBorder: Color.FromArgb(156, 200, 245),   // #9CC8F5
        SuspendedHatch: Color.FromArgb(189, 189, 189), // #BDBDBD
        SuspendedBack: Color.FromArgb(58, 58, 58),     // #3A3A3A
        SuspendedBorder: Color.FromArgb(189, 189, 189),
        Marker: Color.FromArgb(240, 240, 240),         // #F0F0F0
        MarkerOutline: Color.FromArgb(32, 32, 32),     // #202020
        RunOut: Color.FromArgb(242, 139, 130),         // #F28B82
        Today: Color.FromArgb(255, 166, 87),           // #FFA657
        Selection: UiTheme.Dark.Accent);

    public static Palette Current => SystemInformation.HighContrast
        ? new Palette(
            Background: SystemColors.Window,
            AlternateRow: SystemColors.Window,
            Text: SystemColors.WindowText,
            MutedText: SystemColors.GrayText,
            Grid: SystemColors.GrayText,
            ActiveFill: SystemColors.Highlight,
            ActiveBorder: SystemColors.WindowText,
            SuspendedHatch: SystemColors.WindowText,
            SuspendedBack: SystemColors.Window,
            SuspendedBorder: SystemColors.WindowText,
            Marker: SystemColors.WindowText,
            MarkerOutline: SystemColors.Window,
            RunOut: SystemColors.WindowText,
            Today: SystemColors.HotTrack,
            Selection: SystemColors.Highlight)
        : UiTheme.IsDark ? Dark : Light;

    // Line width that follows the text size (1 px at 100 %, 2 px at
    // 200 % scaling).
    public static float StrokeWidth(Font font) => Math.Max(1f, font.Height / 16f);

    public static void DrawActive(Graphics g, RectangleF r, Palette p, float stroke)
    {
        using var fill = new SolidBrush(p.ActiveFill);
        using var pen = new Pen(p.ActiveBorder, stroke);
        g.FillRectangle(fill, r);
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    public static void DrawSuspended(Graphics g, RectangleF r, Palette p, float stroke)
    {
        using var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, p.SuspendedHatch, p.SuspendedBack);
        using var pen = new Pen(p.SuspendedBorder, stroke) { DashStyle = DashStyle.Dash };
        g.FillRectangle(hatch, r);
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    // Filled diamond = new schedule; hollow diamond = taper stage.
    public static void DrawDiamond(Graphics g, PointF center, float size, Palette p, bool filled, float stroke)
    {
        var h = size / 2f;
        PointF[] pts =
        [
            new(center.X, center.Y - h),
            new(center.X + h, center.Y),
            new(center.X, center.Y + h),
            new(center.X - h, center.Y),
        ];
        using var outline = new Pen(p.MarkerOutline, stroke * 3f) { LineJoin = LineJoin.Round };
        using var border = new Pen(p.Marker, stroke * 1.5f) { LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(filled ? p.Marker : p.MarkerOutline);
        g.DrawPolygon(outline, pts);
        g.FillPolygon(fill, pts);
        g.DrawPolygon(border, pts);
    }

    // Downward triangle whose tip touches `tip`, plus a vertical line
    // from the tip down to `lineBottom`.
    public static void DrawRunOut(Graphics g, PointF tip, float size, float lineBottom, Palette p, float stroke)
    {
        using var line = new Pen(p.RunOut, stroke * 2f);
        g.DrawLine(line, tip.X, tip.Y, tip.X, lineBottom);

        var h = size / 2f;
        PointF[] pts =
        [
            new(tip.X - h, tip.Y - size),
            new(tip.X + h, tip.Y - size),
            tip,
        ];
        using var outline = new Pen(p.MarkerOutline, stroke * 2f) { LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(p.RunOut);
        g.DrawPolygon(outline, pts);
        g.FillPolygon(fill, pts);
    }

    public static void DrawTodayLine(Graphics g, float x, float top, float bottom, Palette p, float stroke)
    {
        using var pen = new Pen(p.Today, stroke * 2f) { DashStyle = DashStyle.Dash };
        g.DrawLine(pen, x, top, x, bottom);
    }
}
