using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MedReminder.UI.UiExtensions;

// Renderizza un glyph "Segoe MDL2 Assets" (font di sistema Windows 10+)
// in un Bitmap utilizzabile come ToolStripButton.Image o Icon custom.
//
// "Segoe MDL2 Assets" espone i simboli Fluent/Metro nell'area PUA
// (Private Use Area) del piano BMP di Unicode: es. "" = Add,
// "" = Edit, "" = Refresh, ecc.
// Riferimento completo: https://learn.microsoft.com/windows/apps/design/style/segoe-ui-symbol-font
//
// Il rendering usa GDI+ ad alta qualità: single-pass, anti-aliased
// grid-fit — su schermi HiDPI vale la pena richiamare Create() con
// un size scelto in base al DeviceDpi corrente della form.
internal static class Mdl2Glyph
{
    private const string FontFamily = "Segoe MDL2 Assets";

    // Cache thread-safe per non ricreare la stessa immagine ad ogni
    // rebuild della UI. La chiave include size e colore per supportare
    // temi diversi.
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

            // Un font "Segoe MDL2 Assets" dimensionato a ~75% dell'area
            // lascia margine visivo attorno al glyph. Uso GraphicsUnit.
            // Pixel per essere DPI-neutro (il caller passa già il size
            // corretto).
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
            // Race innocua: se un altro thread ha già inserito la stessa
            // chiave, disponiamo il nostro duplicato e restituiamo il suo.
            if (Cache.TryGetValue(key, out var existing))
            {
                bmp.Dispose();
                return existing;
            }
            Cache[key] = bmp;
        }
        return bmp;
    }

    // Glyphs più usati come costanti nominali: evitano di sparpagliare
    // "" nel codice UI, dove sono impossibili da leggere.
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
