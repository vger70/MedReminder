using System.Text;

namespace MedReminder.Application.Reporting;

// Plain-text form of a PrintableTable, for the clipboard: the title and
// header lines, then one numbered block per row with the first cell as
// its heading and every other cell as "Header: value".
public static class PrintableTableText
{
    public static string Render(PrintableTable table, string newLine = "\r\n")
    {
        ArgumentNullException.ThrowIfNull(table);
        var sb = new StringBuilder();
        sb.Append(table.Title).Append(newLine);
        foreach (var line in table.HeaderLines) sb.Append(line).Append(newLine);
        sb.Append(newLine);

        if (table.Rows.Count == 0)
        {
            sb.Append(table.EmptyMessage).Append(newLine);
            return sb.ToString();
        }

        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            sb.Append(i + 1).Append(". ").Append(row.Cells.Count > 0 ? row.Cells[0] : string.Empty);
            if (row.Detail is not null) sb.Append(" (").Append(row.Detail).Append(')');
            sb.Append(newLine);
            for (var col = 1; col < table.Columns.Count && col < row.Cells.Count; col++)
            {
                var value = row.Cells[col].Replace("\n", "; ");
                sb.Append("   ").Append(table.Columns[col].Header).Append(": ").Append(value).Append(newLine);
            }
            sb.Append(newLine);
        }
        sb.Append(table.Disclaimer);
        return sb.ToString();
    }
}
