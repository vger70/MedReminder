using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MedReminder.UI.UiExtensions;

// Renders a "Segoe MDL2 Assets" glyph (Windows 10+ system font) into
// a Bitmap that can be used as ToolStripButton.Image or as a custom
// Icon.
//
// "Segoe MDL2 Assets" exposes Fluent / Metro symbols in the PUA
// (Private Use Area) of Unicode's BMP: e.g. "" = Add,
// "" = Edit, "" = Refresh, etc.
// Full reference: https://learn.microsoft.com/windows/apps/design/style/segoe-ui-symbol-font
//
// Rendering uses high-quality GDI+: single-pass, anti-aliased
// grid-fit — on HiDPI displays it is worth calling Create() with a
// size chosen based on the form's current DeviceDpi.
internal static class Mdl2Glyph
{
    private const string FontFamily = "Segoe MDL2 Assets";

    // Thread-safe cache so we do not rebuild the same image on
    // every UI rebuild. The key includes size and color to support
    // different themes.
    private static readonly Dictionary<(string glyph, int size, int argb), Bitmap> Cache = new();
    private static readonly object CacheSync = new();

    public static Bitmap Create(string glyph, int size = 20, Color? color = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(glyph);
        if (size < 8) size = 8;
        var col = color ?? SystemColors.ControlText;
        var key = (glyph, size, col.ToArgb());

        lock (CacheSync)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.CompositingQuality = CompositingQuality.HighQuality;

            // A "Segoe MDL2 Assets" font sized at ~75% of the area
            // leaves visual padding around the glyph. Use
            // GraphicsUnit.Pixel to stay DPI-neutral (the caller
            // already passes the correct size).
            using var font = new Font(FontFamily, size * 0.72f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(col);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
        }

        lock (CacheSync)
        {
            // Harmless race: if another thread has already inserted
            // the same key, dispose our duplicate and return theirs.
            if (Cache.TryGetValue(key, out var existing))
            {
                bmp.Dispose();
                return existing;
            }
            Cache[key] = bmp;
        }
        return bmp;
    }

    // Most-used glyphs as named constants: avoid scattering PUA
    // characters ("", etc.) through the UI code, where they
    // are impossible to read.
    public static class Glyphs
    {
        public const string Add = "";           // Add / Plus
        public const string Edit = "";          // Edit
        public const string Delete = "";        // Delete
        public const string Refresh = "";       // Refresh
        public const string Sync = "";          // Sync
        public const string Settings = "";      // Setting
        public const string Print = "";         // Print
        public const string Save = "";          // Save
        public const string Search = "";        // Search
        public const string Info = "";          // Info
        public const string Help = "";          // Help
        public const string Home = "";          // Home
        public const string Calendar = "";      // Calendar
        public const string Document = "";      // Document
        public const string CheckMark = "";     // CheckMark
        public const string History = "";       // History
        public const string Warning = "";       // Warning
        public const string HealthReport = "";  // HealthReport
        public const string Notebook = "";      // NotebookEdit
        public const string Contact = "";       // Contact
        public const string Cancel = "";        // Cancel
        public const string Play = "";          // Play
        public const string StatusCircle = "";  // StatusCircleOuter
        public const string Package = "";       // Package
    }
}
