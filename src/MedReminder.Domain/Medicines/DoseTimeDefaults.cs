using System.Globalization;

namespace MedReminder.Domain.Medicines;

// Times of day of a medicine without slots, by number of administrations
// per day (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md §4.2): its
// daily quantity is split in equal doses at these times. Display only,
// like DoseTimePreset. Stored rows override the built-in values; more
// than MaxAdministrations per day has no times (end-of-day booking).
public sealed class DoseTimeDefault
{
    public const int MaxAdministrations = 4;

    public required int AdministrationsPerDay { get; init; }

    // "HH:mm" values separated by ';', in day order.
    public required string Times { get; set; }

    public static readonly IReadOnlyDictionary<int, IReadOnlyList<TimeOnly>> BuiltIn =
        new Dictionary<int, IReadOnlyList<TimeOnly>>
        {
            [1] = [new(8, 0)],
            [2] = [new(8, 0), new(20, 0)],
            [3] = [new(8, 0), new(13, 0), new(20, 0)],
            [4] = [new(8, 0), new(12, 0), new(16, 0), new(20, 0)],
        };

    public static string Format(IEnumerable<TimeOnly> times)
        => string.Join(';', times.Order().Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)));

    // Null when the text is not exactly `count` valid times.
    public static IReadOnlyList<TimeOnly>? Parse(string? text, int count)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != count) return null;
        var times = new List<TimeOnly>(count);
        foreach (var part in parts)
        {
            if (!TimeOnly.TryParseExact(part, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time))
            {
                return null;
            }
            times.Add(time);
        }
        return [.. times.Order()];
    }
}
