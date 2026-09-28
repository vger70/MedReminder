using System.Globalization;
using System.Text.RegularExpressions;

namespace MedReminder.Application.Export;

// File name of an automatic cloud snapshot (C.3+,
// docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.3):
// "medreminder-<profileId>-<yyyyMMdd-HHmmss>.mrz", UTC stamp. profileId
// is a Guid "N" (32 hex chars) or the literal "default" from the V1→V2
// migration. One definition for the backup host that writes the name,
// the retention that prunes by it, and the restore list that reads it,
// so the three cannot drift. A name that does not match exactly (a
// hand-copied or renamed archive, a manual C.3 export) is never pruned
// and carries no profile or date.
public static partial class CloudSnapshotName
{
    private const string StampFormat = "yyyyMMdd-HHmmss";

    [GeneratedRegex(@"^medreminder-(?<profileId>[0-9a-fA-F]{32}|default)-(?<stamp>\d{8}-\d{6})\.mrz$")]
    private static partial Regex Pattern();

    // Invariant culture: a non-Gregorian current calendar would write a
    // year that TryParse reads back wrong.
    public static string Create(string profileId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return $"medreminder-{profileId}-{createdAt.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture)}"
            + ExportFormat.ArchiveExtension;
    }

    public static bool IsMatch(string fileName) => Pattern().IsMatch(fileName);

    public static bool TryParse(string fileName, out string profileId, out DateTimeOffset createdAtUtc)
    {
        var match = Pattern().Match(fileName);
        if (match.Success && DateTime.TryParseExact(match.Groups["stamp"].Value, StampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var stamp))
        {
            profileId = match.Groups["profileId"].Value;
            createdAtUtc = new DateTimeOffset(stamp, TimeSpan.Zero);
            return true;
        }
        profileId = string.Empty;
        createdAtUtc = default;
        return false;
    }
}
