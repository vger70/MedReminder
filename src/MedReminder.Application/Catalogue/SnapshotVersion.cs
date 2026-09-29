namespace MedReminder.Application.Catalogue;

// Ordering rule for reference-catalogue snapshot versions
// (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md §5.2).
//
// Snapshot versions are `yyyymm` labels ("202609"), which sort
// lexically. A newer label replaces the country's rows; an equal or
// older one is a no-op, so an embedded snapshot shipped with the app
// never downgrades a newer one downloaded from the remote feed.
// Labels that are not `yyyymm` keep the original rule: any difference
// counts as newer.
public static class SnapshotVersion
{
    public static bool IsYearMonth(string? version)
    {
        if (version is null || version.Length != 6)
        {
            return false;
        }

        foreach (var c in version)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        var month = (version[4] - '0') * 10 + (version[5] - '0');
        return month is >= 1 and <= 12;
    }

    // True when `candidate` should replace `current`. A null `current`
    // means nothing is imported yet for the country.
    public static bool IsNewer(string candidate, string? current)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);

        if (current is null)
        {
            return true;
        }

        if (IsYearMonth(candidate) && IsYearMonth(current))
        {
            return string.CompareOrdinal(candidate, current) > 0;
        }

        return !string.Equals(candidate, current, StringComparison.Ordinal);
    }
}
