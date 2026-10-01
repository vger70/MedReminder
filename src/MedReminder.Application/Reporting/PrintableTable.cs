namespace MedReminder.Application.Reporting;

// Renderer-neutral content of a printed table: every string is already
// localized and formatted, so the UI only lays it out (title and header
// lines on every page, one row per item, disclaimer and page number in
// the footer). Used by the coverage planner; the therapy card keeps its
// own model (TherapyCard).
public sealed record PrintableTable(
    string Title,
    IReadOnlyList<string> HeaderLines,
    IReadOnlyList<PrintableColumn> Columns,
    IReadOnlyList<PrintableRow> Rows,
    string EmptyMessage,
    string Disclaimer);

// Width is relative to the other columns. The first column is drawn in
// bold, with the row's Detail in small type under it.
public sealed record PrintableColumn(string Header, float Width);

// Cells has one entry per column; a cell may hold several lines.
public sealed record PrintableRow(IReadOnlyList<string> Cells, string? Detail = null);
