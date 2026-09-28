using System.Drawing.Printing;
using MedReminder.Application.Reporting;

namespace MedReminder.UI.Printing;

// Printed layout of the therapy card (EVOLUTION-PROPOSALS §4.4): a
// table with one row per active medicine, the page header and the
// table header repeated on every page, and the disclaimer plus page
// number in the footer of every page. The same document feeds the
// print preview, a physical printer and "Microsoft Print to PDF".
//
// Layout happens on the first PrintPage of each print cycle, when
// the printer Graphics is available; BeginPrint resets it because the
// preview renders the document more than once.
internal sealed class TherapyCardPrintDocument : PrintDocument
{
    private const float CellPadding = 6f;   // hundredths of an inch
    private const float BlockSpacing = 12f;

    private readonly TherapyCard _card;
    private readonly PaperKind _paper;
    private readonly string _pageFormat;

    private Font? _titleFont;
    private Font? _textFont;
    private Font? _boldFont;
    private Font? _smallFont;
    private float[] _columnWidths = [];
    private List<float> _rowHeights = [];
    private IReadOnlyList<TherapyCardPage>? _pages;
    private float _tableHeaderHeight;
    private int _pageIndex;

    // pageFormat takes the 1-based page number as {0}.
    public TherapyCardPrintDocument(TherapyCard card, PaperKind paper, string pageFormat)
    {
        _card = card;
        _paper = paper;
        _pageFormat = pageFormat;
        DocumentName = "MedReminder — Therapy card";
    }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        DisposeFonts();
        _titleFont = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point);
        _textFont = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        _boldFont = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        _smallFont = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        _pages = null;
        _pageIndex = 0;
    }

    protected override void OnEndPrint(PrintEventArgs e)
    {
        base.OnEndPrint(e);
        DisposeFonts();
    }

    // Applies the chosen paper to every page, whatever the selected
    // printer defaults to. Falls back to a custom size when the
    // driver does not list the paper kind.
    protected override void OnQueryPageSettings(QueryPageSettingsEventArgs e)
    {
        base.OnQueryPageSettings(e);
        e.PageSettings.PaperSize = FindPaperSize(e.PageSettings.PrinterSettings, _paper);
        e.PageSettings.Landscape = false;
        e.PageSettings.Margins = new Margins(60, 60, 60, 60);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        var g = e.Graphics!;
        RectangleF bounds = e.MarginBounds;

        var headerHeight = MeasureHeader(g, bounds.Width);
        var footerHeight = MeasureFooter(g, bounds.Width);
        _pages ??= Layout(g, bounds, headerHeight, footerHeight);

        DrawHeader(g, bounds);
        var y = bounds.Top + headerHeight;

        if (_card.Rows.Count == 0)
        {
            g.DrawString(_card.EmptyMessage, _textFont!, Brushes.Black, bounds.Left, y);
        }
        else
        {
            var page = _pages[_pageIndex];
            y = DrawTableHeader(g, bounds.Left, y);
            var rowsBottom = bounds.Bottom - footerHeight;
            for (var i = page.FirstRow; i < page.FirstRow + page.RowCount; i++)
            {
                var height = Math.Min(_rowHeights[i], rowsBottom - y);
                DrawRow(g, _card.Rows[i], bounds.Left, y, height);
                y += height;
            }
        }

        DrawFooter(g, bounds, footerHeight);

        _pageIndex++;
        e.HasMorePages = _pageIndex < _pages.Count;
    }

    private IReadOnlyList<TherapyCardPage> Layout(
        Graphics g, RectangleF bounds, float headerHeight, float footerHeight)
    {
        _columnWidths = ColumnFractions(_card.IncludesNotes)
            .Select(f => f * bounds.Width)
            .ToArray();
        _tableHeaderHeight = HeaderCells()
            .Select((text, col) => MeasureCell(g, text, _boldFont!, col))
            .Max();
        _rowHeights = _card.Rows.Select(r => MeasureRow(g, r)).ToList();

        var available = bounds.Height - headerHeight - footerHeight - _tableHeaderHeight;
        return TherapyCardPagination.Paginate(_rowHeights, Math.Max(1f, available));
    }

    // Index, Medicine, Dosage, Period, Doctor[, Notes] as fractions of
    // the printable width.
    private static float[] ColumnFractions(bool includesNotes) => includesNotes
        ? [0.05f, 0.25f, 0.27f, 0.14f, 0.13f, 0.16f]
        : [0.05f, 0.30f, 0.32f, 0.16f, 0.17f];

    private IEnumerable<string> HeaderCells()
    {
        var c = _card.Columns;
        yield return "#";
        yield return c.Medicine;
        yield return c.Dosage;
        yield return c.Period;
        yield return c.Doctor;
        if (_card.IncludesNotes) yield return c.Notes;
    }

    private IEnumerable<string> RowCells(TherapyCardRow row)
    {
        yield return row.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return row.Name;   // active ingredient is drawn below it
        yield return string.Join("\n", row.DosageLines);
        yield return string.Join("\n", row.PeriodLines);
        yield return row.Doctor ?? string.Empty;
        if (_card.IncludesNotes) yield return row.Notes ?? string.Empty;
    }

    private float MeasureRow(Graphics g, TherapyCardRow row)
    {
        var height = 0f;
        var col = 0;
        foreach (var text in RowCells(row))
        {
            var font = col == 1 ? _boldFont! : _textFont!;
            var h = MeasureCell(g, text, font, col);
            if (col == 1 && row.ActiveIngredient is not null)
            {
                h += MeasureText(g, row.ActiveIngredient, _smallFont!, CellTextWidth(col));
            }
            height = Math.Max(height, h);
            col++;
        }
        return height;
    }

    private float MeasureCell(Graphics g, string text, Font font, int col)
        => MeasureText(g, text, font, CellTextWidth(col)) + 2 * CellPadding;

    private float CellTextWidth(int col) => Math.Max(1f, _columnWidths[col] - 2 * CellPadding);

    private static float MeasureText(Graphics g, string text, Font font, float width)
        => string.IsNullOrEmpty(text)
            ? font.GetHeight(g)
            : g.MeasureString(text, font, (int)Math.Ceiling(width)).Height;

    private float MeasureHeader(Graphics g, float width)
    {
        var h = MeasureText(g, _card.Title, _titleFont!, width);
        if (_card.ProfileLine is not null) h += MeasureText(g, _card.ProfileLine, _textFont!, width);
        h += MeasureText(g, _card.DateLine, _textFont!, width);
        return h + BlockSpacing;
    }

    private float MeasureFooter(Graphics g, float width)
        => BlockSpacing + MeasureText(g, _card.Disclaimer, _smallFont!, width * 0.8f);

    private void DrawHeader(Graphics g, RectangleF bounds)
    {
        var y = bounds.Top;
        y += DrawText(g, _card.Title, _titleFont!, bounds.Left, y, bounds.Width);
        if (_card.ProfileLine is not null)
        {
            y += DrawText(g, _card.ProfileLine, _textFont!, bounds.Left, y, bounds.Width);
        }
        DrawText(g, _card.DateLine, _textFont!, bounds.Left, y, bounds.Width);
    }

    private float DrawTableHeader(Graphics g, float left, float top)
    {
        var width = _columnWidths.Sum();
        g.FillRectangle(Brushes.Gainsboro, left, top, width, _tableHeaderHeight);
        var x = left;
        var col = 0;
        foreach (var text in HeaderCells())
        {
            DrawText(g, text, _boldFont!, x + CellPadding, top + CellPadding, CellTextWidth(col));
            g.DrawRectangle(Pens.Gray, x, top, _columnWidths[col], _tableHeaderHeight);
            x += _columnWidths[col];
            col++;
        }
        return top + _tableHeaderHeight;
    }

    private void DrawRow(Graphics g, TherapyCardRow row, float left, float top, float height)
    {
        var state = g.Save();
        var x = left;
        var col = 0;
        foreach (var text in RowCells(row))
        {
            var cell = new RectangleF(x, top, _columnWidths[col], height);
            g.SetClip(cell);
            var textTop = top + CellPadding;
            var font = col == 1 ? _boldFont! : _textFont!;
            textTop += DrawText(g, text, font, x + CellPadding, textTop, CellTextWidth(col));
            if (col == 1 && row.ActiveIngredient is not null)
            {
                DrawText(g, row.ActiveIngredient, _smallFont!, x + CellPadding, textTop, CellTextWidth(col));
            }
            g.ResetClip();
            g.DrawRectangle(Pens.Gray, cell.X, cell.Y, cell.Width, cell.Height);
            x += _columnWidths[col];
            col++;
        }
        g.Restore(state);
    }

    private void DrawFooter(Graphics g, RectangleF bounds, float height)
    {
        var top = bounds.Bottom - height + BlockSpacing;
        DrawText(g, _card.Disclaimer, _smallFont!, bounds.Left, top, bounds.Width * 0.8f);

        var pageText = string.Format(System.Globalization.CultureInfo.CurrentCulture, _pageFormat, _pageIndex + 1);
        var size = g.MeasureString(pageText, _smallFont!);
        g.DrawString(pageText, _smallFont!, Brushes.Black, bounds.Right - size.Width, top);
    }

    // Draws wrapped text and returns the height it used.
    private static float DrawText(Graphics g, string text, Font font, float x, float y, float width)
    {
        var height = MeasureText(g, text, font, width);
        if (!string.IsNullOrEmpty(text))
        {
            g.DrawString(text, font, Brushes.Black, new RectangleF(x, y, width, height));
        }
        return height;
    }

    private static PaperSize FindPaperSize(PrinterSettings printer, PaperKind kind)
    {
        foreach (PaperSize size in printer.PaperSizes)
        {
            if (size.Kind == kind) return size;
        }
        // Sizes in hundredths of an inch: A4 is 210 × 297 mm.
        return kind == PaperKind.Letter
            ? new PaperSize("Letter", 850, 1100)
            : new PaperSize("A4", 827, 1169);
    }

    private void DisposeFonts()
    {
        _titleFont?.Dispose();
        _textFont?.Dispose();
        _boldFont?.Dispose();
        _smallFont?.Dispose();
        _titleFont = _textFont = _boldFont = _smallFont = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeFonts();
        base.Dispose(disposing);
    }
}
