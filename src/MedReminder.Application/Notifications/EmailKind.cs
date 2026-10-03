namespace MedReminder.Application.Notifications;

// What an automated email is about (anticipated in
// docs/analysis/ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md §4.4, added with
// docs/notes/EVOLUTION-PROPOSALS-2.md §3.8): decides whether the
// caregiver gets a copy. Member names are stored in the CaregiverEmails
// profile setting: do not rename them.
public enum EmailKind
{
    LowStock,
    DoseReminder,
    Prescription,
    Deadline,
    Shortage,
    // The weekly stock summary: to the caregiver only.
    Digest,
    // Packages expiring or expired (docs/analysis/ANALYSIS-PACKAGE-EXPIRY.md §4.2).
    PackageExpiry,
}

// The CaregiverEmails profile setting: which kinds of email are copied
// to the caregiver. "" (the default, and every file written before the
// setting) copies every kind, as before; "None" copies none.
public static class CaregiverEmails
{
    public const string NoneValue = "None";

    // The kinds a caregiver can choose, in display order.
    public static readonly IReadOnlyList<EmailKind> Choices =
        [EmailKind.LowStock, EmailKind.DoseReminder, EmailKind.Prescription, EmailKind.Deadline, EmailKind.Shortage,
            EmailKind.PackageExpiry];

    public static IReadOnlySet<EmailKind> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Choices.ToHashSet();
        if (string.Equals(value.Trim(), NoneValue, StringComparison.Ordinal)) return new HashSet<EmailKind>();
        var kinds = new HashSet<EmailKind>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A name from a newer app is ignored.
            if (Enum.TryParse<EmailKind>(part, ignoreCase: false, out var kind) && Choices.Contains(kind)) kinds.Add(kind);
        }
        return kinds;
    }

    public static string Format(IEnumerable<EmailKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var chosen = Choices.Where(kinds.Contains).ToList();
        if (chosen.Count == Choices.Count) return string.Empty;
        return chosen.Count == 0 ? NoneValue : string.Join(",", chosen);
    }

    // Whether an email of that kind goes to the caregiver too. An email
    // with no kind is copied, as every email was before the setting.
    public static bool Copies(string? value, EmailKind? kind)
        => kind is not { } k || Parse(value).Contains(k);
}

// The CaregiverDigest profile setting.
public static class CaregiverDigestFrequency
{
    public const string Off = "Off";
    public const string Weekly = "Weekly";

    public const int WeeklyDays = 7;

    public static bool IsWeekly(string? value) => string.Equals(value?.Trim(), Weekly, StringComparison.Ordinal);
}
