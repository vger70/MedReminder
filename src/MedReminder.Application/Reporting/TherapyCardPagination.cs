namespace MedReminder.Application.Reporting;

// Rows [FirstRow, FirstRow + RowCount) of TherapyCard.Rows drawn on
// one printed page.
public sealed record TherapyCardPage(int FirstRow, int RowCount);

// Splits the table rows into pages from their measured heights. Kept
// free of System.Drawing so the rule is unit-testable: the UI measures
// each row with the printer Graphics and passes the heights here.
public static class TherapyCardPagination
{
    // availableHeight is the space left for rows on every page once
    // the page header, the repeated table header and the footer are
    // drawn; all values use the same unit. A row never splits across
    // pages; a row taller than a whole page gets a page of its own
    // (the renderer clips it). An empty table still yields one page,
    // where the renderer draws the "no active medicines" message.
    public static IReadOnlyList<TherapyCardPage> Paginate(
        IReadOnlyList<float> rowHeights, float availableHeight)
    {
        ArgumentNullException.ThrowIfNull(rowHeights);
        if (availableHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(availableHeight), "Available height must be positive.");
        }

        var pages = new List<TherapyCardPage>();
        var first = 0;
        var used = 0f;
        for (var i = 0; i < rowHeights.Count; i++)
        {
            var h = Math.Max(0f, rowHeights[i]);
            if (i > first && used + h > availableHeight)
            {
                pages.Add(new TherapyCardPage(first, i - first));
                first = i;
                used = 0f;
            }
            used += h;
        }
        pages.Add(new TherapyCardPage(first, rowHeights.Count - first));
        return pages;
    }
}
