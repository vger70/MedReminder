using System.Globalization;

namespace MedReminder.Application.Catalogue;

// Ordering rule for reference-catalogue snapshot versions
// (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md §5.2, §11.2).
//
// A label is `yyyymm` ("202609"), optionally followed by the UTC time
// the remote feed built the archive: `yyyymm+yyyyMMddTHHmmssZ`
// ("202610+20261005T030012Z"). Embedded snapshots carry the bare month;
// remote imports carry the suffix, so an archive republished in the
// same month is newer than the one it replaces.
//
// Order: month first, then suffix; within a month a bare label is older
// than any suffixed one. A newer label replaces the country's rows; an
// equal or older one is a no-op, so an embedded snapshot never
// downgrades a newer remote import. Labels outside this format keep the
// original rule: any difference counts as newer.
public static class SnapshotVersion
{
    private const string SuffixFormat = "yyyyMMdd'T'HHmmss'Z'";
    private const int SuffixLength = 16;

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

    // The label stored for a remote import: the month alone when the
    // build time is unknown, otherwise month + UTC build time (seconds).
    public static string Compose(string yearMonth, DateTimeOffset? generated)
    {
        if (!IsYearMonth(yearMonth))
        {
            throw new ArgumentException("Expected a yyyymm version.", nameof(yearMonth));
        }

        return generated is { } value
            ? yearMonth + "+" + value.UtcDateTime.ToString(SuffixFormat, CultureInfo.InvariantCulture)
            : yearMonth;
    }

    // Splits a label into its month and optional suffix. False for
    // labels outside the `yyyymm[+yyyyMMddTHHmmssZ]` format.
    public static bool TryParse(string? label, out string yearMonth, out string? suffix)
    {
        yearMonth = string.Empty;
        suffix = null;
        if (label is null)
        {
            return false;
        }

        var plus = label.IndexOf('+');
        var month = plus < 0 ? label : label[..plus];
        if (!IsYearMonth(month))
        {
            return false;
        }

        if (plus >= 0)
        {
            var tail = label[(plus + 1)..];
            if (tail.Length != SuffixLength
                || !DateTime.TryParseExact(tail, SuffixFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _))
            {
                return false;
            }
            suffix = tail;
        }

        yearMonth = month;
        return true;
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

        if (TryParse(candidate, out var candidateMonth, out var candidateSuffix)
            && TryParse(current, out var currentMonth, out var currentSuffix))
        {
            var byMonth = string.CompareOrdinal(candidateMonth, currentMonth);
            if (byMonth != 0)
            {
                return byMonth > 0;
            }

            return (candidateSuffix, currentSuffix) switch
            {
                (null, _) => false,
                (not null, null) => true,
                // Fixed-width UTC timestamps sort lexically.
                _ => string.CompareOrdinal(candidateSuffix, currentSuffix) > 0,
            };
        }

        return !string.Equals(candidate, current, StringComparison.Ordinal);
    }
}
