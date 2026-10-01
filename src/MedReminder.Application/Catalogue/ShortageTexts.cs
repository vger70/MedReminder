using MedReminder.Application.Abstractions;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Localized wording of a shortage (docs/notes/EVOLUTION-PROPOSALS-2.md
// §3.3). Factual and neutral: it reports what the publisher lists, never
// names a substitute and always sends the user to the doctor or the
// pharmacist. The expected end is "expected" only: the publisher keeps
// a medicine listed until the end is confirmed.
public static class ShortageTexts
{
    // Short form for the medicine list.
    public static string Display(ShortageNotice notice, ILocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(loc);
        return notice.State == ShortageState.Expected
            ? loc.Get("Shortage.Display.Expected", Date(notice.Entry.Start, loc))
            : loc.Get("Shortage.Display.Current");
    }

    // Full text, one sentence per line.
    public static string Detail(ShortageNotice notice, DateOnly listDate, ILocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(loc);
        var entry = notice.Entry;
        var lines = new List<string>
        {
            notice.State == ShortageState.Expected
                ? loc.Get("Shortage.Detail.Expected", Date(entry.Start, loc))
                : loc.Get("Shortage.Detail.Current", Date(entry.Start, loc)),
            entry.ExpectedEnd is { } end
                ? loc.Get("Shortage.Detail.End", Date(end, loc))
                : loc.Get("Shortage.Detail.NoEnd"),
            loc.Get("Shortage.Detail.Reason", loc.Get("Shortage.Reason." + entry.Reason)),
        };
        if (entry.EquivalentAvailable) lines.Add(loc.Get("Shortage.Detail.Equivalent"));
        lines.Add(loc.Get("Shortage.Detail.Advice"));
        lines.Add(loc.Get("Shortage.Detail.Source", Date(listDate, loc)));
        return string.Join(Environment.NewLine, lines);
    }

    public static (string Title, string Body) Notification(string medicineName, ShortageNotice notice,
        ILocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(loc);
        var start = Date(notice.Entry.Start, loc);
        return notice.State == ShortageState.Expected
            ? (loc.Get("Notifications.Shortage.Title.Expected", medicineName),
                loc.Get("Notifications.Shortage.Body.Expected", medicineName, start))
            : (loc.Get("Notifications.Shortage.Title", medicineName),
                loc.Get("Notifications.Shortage.Body", medicineName, start));
    }

    private static string Date(DateOnly day, ILocalizationService loc) => day.ToString("d", loc.CurrentCulture);
}
