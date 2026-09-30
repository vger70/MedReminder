namespace MedReminder.Application.Overview;

// Status groups of the summary cards above the main grid
// (docs/analysis/ANALYSIS-UI-MODERNIZATION.md §5.1).
public enum MedicineListBucket
{
    All = 0,
    Empty = 1,
    Warning = 2,
    Suspended = 3,
}

// Counts shown on the summary cards, taken from the rows the grid can
// show (active only, unless inactive medicines are shown).
public sealed record MedicineListSummary(int Empty, int Warning, int Suspended, int All);

// Filtering of the main window's medicine list: pure functions over the
// rows MedicineOverviewLoader already returns, so the cards and the
// grid always agree and no new query is needed.
public static class MedicineListFilter
{
    // Rows the grid can show before any card or search filter.
    public static IReadOnlyList<MedicineListItem> Visible(IEnumerable<MedicineListItem> rows, bool showInactive)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return showInactive ? rows.ToList() : rows.Where(r => r.IsActive).ToList();
    }

    public static MedicineListSummary Summarize(IReadOnlyCollection<MedicineListItem> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        return new MedicineListSummary(
            Empty: visible.Count(r => r.Status == MedicineRowStatus.Empty),
            Warning: visible.Count(r => r.Status == MedicineRowStatus.Warning),
            Suspended: visible.Count(r => r.Status == MedicineRowStatus.Suspended),
            All: visible.Count);
    }

    // Keeps the rows of the chosen card whose name contains the search
    // text, ignoring case and surrounding spaces; load order is kept.
    public static List<MedicineListItem> Apply(
        IEnumerable<MedicineListItem> visible, MedicineListBucket bucket, string? search)
    {
        ArgumentNullException.ThrowIfNull(visible);
        var text = search?.Trim();
        return visible
            .Where(r => IsIn(r, bucket))
            .Where(r => string.IsNullOrEmpty(text)
                || r.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    public static bool IsIn(MedicineListItem row, MedicineListBucket bucket)
    {
        ArgumentNullException.ThrowIfNull(row);
        return bucket switch
        {
            MedicineListBucket.Empty => row.Status == MedicineRowStatus.Empty,
            MedicineListBucket.Warning => row.Status == MedicineRowStatus.Warning,
            MedicineListBucket.Suspended => row.Status == MedicineRowStatus.Suspended,
            _ => true,
        };
    }
}
