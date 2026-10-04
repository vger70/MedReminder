namespace MedReminder.Domain.Catalogue;

// A national list of medicines in temporary shortage (the Italian AIFA
// "elenco dei farmaci carenti"; docs/notes/EVOLUTION-PROPOSALS-2.md
// §3.3), keyed on the package code (the 9-digit AIC). Supply information
// only: it names no substitute and makes no clinical statement.
public sealed class ShortageList
{
    private readonly Dictionary<string, ShortageEntry> _entries;

    public ShortageList(CountryCode country, DateOnly listDate, IEnumerable<ShortageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Country = country;
        ListDate = listDate;
        _entries = new Dictionary<string, ShortageEntry>(StringComparer.Ordinal);
        foreach (var entry in entries) _entries[entry.Code] = entry;
    }

    public CountryCode Country { get; }

    // The date the publisher gives the list.
    public DateOnly ListDate { get; }

    public int Count => _entries.Count;

    public ShortageEntry? Find(string? code)
        => code is not null && _entries.TryGetValue(code.Trim(), out var entry) ? entry : null;

    // The state of a package code on a day; null when it is not listed.
    // A listed shortage stays current after its expected end: the
    // publisher keeps it listed until the end is confirmed.
    public ShortageNotice? NoticeFor(string? code, DateOnly today)
        => Find(code) is { } entry
            ? new ShortageNotice(entry, entry.Start > today ? ShortageState.Expected : ShortageState.Current)
            : null;
}

public sealed record ShortageEntry(
    string Code,
    DateOnly Start,
    DateOnly? ExpectedEnd,
    bool EquivalentAvailable,
    ShortageReason Reason);

public sealed record ShortageNotice(ShortageEntry Entry, ShortageState State);

public enum ShortageState
{
    // Started: the medicine may be hard to find.
    Current,
    // Announced with a later start date.
    Expected,
}

public enum ShortageReason
{
    Other,
    Production,
    Demand,
    Withdrawn,
    Suspended,
    Commercial,
    Regulatory,
}
