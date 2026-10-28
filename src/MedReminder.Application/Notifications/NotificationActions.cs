using System.Globalization;

namespace MedReminder.Application.Notifications;

// What a Windows notification is about, so the toast can offer actions
// on it (docs/notes/EVOLUTION-PROPOSALS-2.md §3.4). Carries identifiers
// only: a toast's arguments are stored by Windows and must not hold a
// medicine name or any other health data.
// HasRepeatablePrescription (low stock only): the medicine has a
// repeatable prescription with dispensations left, so the toast offers to
// open it rather than to prepare a request for a new one.
// DeadlineId (deadline reminder only): the deadline, so a device that
// replaces a notification with the same identity keeps two deadlines of
// the profile apart (MedicineId is Guid.Empty for both).
// Notice (daily notices only): the notice itself, the same on every day it
// is planned or shown (NoticeKeys), so a host that hands notices to the
// platform ahead can tell that the device already delivered it on an
// earlier day. Null for a dose reminder, a new notice every day.
public sealed record NotificationTarget(
    NotificationKind Kind, Guid MedicineId, TimeOnly? SlotTime = null, bool HasRepeatablePrescription = false,
    Guid? DeadlineId = null, string? Notice = null)
{
    public static NotificationTarget DoseReminder(Guid medicineId, TimeOnly slotTime)
        => new(NotificationKind.DoseReminder, medicineId, slotTime);

    public static NotificationTarget LowStock(Guid medicineId, bool hasRepeatablePrescription = false,
        string? notice = null)
        => new(NotificationKind.LowStock, medicineId, HasRepeatablePrescription: hasRepeatablePrescription,
            Notice: notice);

    public static NotificationTarget Prescription(Guid medicineId, string? notice = null)
        => new(NotificationKind.Prescription, medicineId, Notice: notice);

    public static NotificationTarget Shortage(Guid medicineId) => new(NotificationKind.Shortage, medicineId);

    // Guid.Empty for a deadline of the profile.
    public static NotificationTarget Deadline(Guid? medicineId, Guid? deadlineId = null, string? notice = null)
        => new(NotificationKind.Deadline, medicineId ?? Guid.Empty, DeadlineId: deadlineId, Notice: notice);

    public static NotificationTarget PackageExpiry(Guid medicineId, string? notice = null)
        => new(NotificationKind.PackageExpiry, medicineId, Notice: notice);
}

// The identity of a daily notice across days (NotificationTarget.Notice):
// what its event records, so two keys are equal only for the same notice.
// Low stock: the medicine, its stock epoch (the number and, when known,
// the fact that opened it: a number may come back after a retraction) and
// the stage. Package expiry: the stage and each package noticed with its
// expiry. "Valid until": the prescription and the date. Deadline: the
// deadline and its due date.
//
// A key is a head and, after its last ':', one or more items separated by
// ','. Only a package expiry notice has several: the packages of one
// notice may have come due on different days, each with a notice of its
// own (Covers).
public static class NoticeKeys
{
    public static string LowStock(Guid medicineId, int stockEpoch, Guid? epochFactId, int stage)
        => string.Create(CultureInfo.InvariantCulture,
            $"stock:{medicineId:N}:{stockEpoch}:{(epochFactId is { } fact ? fact.ToString("N") : "-")}:{stage}");

    public static string PackageExpiry(int stage, IEnumerable<(Guid PackageId, DateOnly Expiry)> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var items = packages
            .Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.PackageId:N}@{p.Expiry:yyyy-MM-dd}"))
            .Order(StringComparer.Ordinal);
        return string.Create(CultureInfo.InvariantCulture, $"expiry:{stage}:{string.Join(",", items)}");
    }

    // Whether the notices `delivered` (their keys) together cover `notice`:
    // every item of it is in a delivered notice with the same head. For a
    // key of one item, whether that key was delivered.
    public static bool Covers(IEnumerable<string> delivered, string notice)
    {
        ArgumentNullException.ThrowIfNull(delivered);
        ArgumentNullException.ThrowIfNull(notice);
        var (head, items) = Split(notice);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in delivered)
        {
            var (otherHead, otherItems) = Split(key);
            if (string.Equals(head, otherHead, StringComparison.Ordinal)) covered.UnionWith(otherItems);
        }
        return items.All(covered.Contains);
    }

    private static (string Head, string[] Items) Split(string key)
    {
        var at = key.LastIndexOf(':');
        return at < 0 ? (key, [string.Empty]) : (key[..at], key[(at + 1)..].Split(','));
    }

    public static string Prescription(Guid prescriptionId, DateOnly validUntil)
        => string.Create(CultureInfo.InvariantCulture, $"prescription:{prescriptionId:N}:{validUntil:yyyy-MM-dd}");

    public static string Deadline(Guid deadlineId, DateOnly dueOn)
        => string.Create(CultureInfo.InvariantCulture, $"deadline:{deadlineId:N}:{dueOn:yyyy-MM-dd}");
}

public enum NotificationKind
{
    DoseReminder,
    LowStock,
    Prescription,
    Shortage,
    Deadline,
    PackageExpiry,
}

// What the user asked for from a toast. Only actions with no effect on
// the recorded facts: recording an intake from a toast would cancel the
// automatic consumption of the whole day (LedgerDeriver rule 2) when the
// other doses of the day are not recorded too.
public enum NotificationActionKind
{
    // Body click: the main window, on the medicine.
    Open,
    // Body click on a prescription reminder, or low-stock button when a
    // repeatable prescription has dispensations left: the prescriptions
    // window.
    OpenPrescriptions,
    // Body click on a deadline reminder: the deadlines window.
    OpenDeadlines,
    // Body click on a package expiry notice: the packages of the medicine.
    OpenPackages,
    // Dose reminder button: the same reminder again in SnoozeMinutes.
    Snooze,
    // Low-stock button: the prescription request draft of the medicine.
    RequestPrescription,
}

public sealed record NotificationAction(
    NotificationActionKind Kind,
    string ProfileId,
    Guid MedicineId,
    TimeOnly? SlotTime = null);

// Toast arguments of the actions, as key/value pairs. Part of the
// contract with toasts already shown or scheduled by Windows: keep the
// keys and values stable.
public static class NotificationActionArguments
{
    public const int SnoozeMinutes = 15;

    public const string ActionKey = "action";
    public const string ProfileKey = "profile";
    public const string MedicineKey = "medicine";
    public const string SlotKey = "slot";

    private const string TimeFormat = "HH:mm";

    // The arguments of the toast body for a target.
    public static IReadOnlyDictionary<string, string> ForBody(NotificationTarget target, string profileId)
        => Format(new NotificationAction(
            target.Kind switch
            {
                NotificationKind.Prescription => NotificationActionKind.OpenPrescriptions,
                NotificationKind.Deadline => NotificationActionKind.OpenDeadlines,
                NotificationKind.PackageExpiry => NotificationActionKind.OpenPackages,
                _ => NotificationActionKind.Open,
            },
            profileId, target.MedicineId, target.SlotTime));

    // The buttons a target offers, in order.
    public static IReadOnlyList<NotificationAction> ButtonsFor(NotificationTarget target, string profileId)
        => target switch
        {
            { Kind: NotificationKind.DoseReminder, SlotTime: { } time } =>
                [new NotificationAction(NotificationActionKind.Snooze, profileId, target.MedicineId, time)],
            { Kind: NotificationKind.LowStock, HasRepeatablePrescription: true } =>
                [new NotificationAction(NotificationActionKind.OpenPrescriptions, profileId, target.MedicineId)],
            { Kind: NotificationKind.LowStock } =>
                [new NotificationAction(NotificationActionKind.RequestPrescription, profileId, target.MedicineId)],
            _ => [],
        };

    public static IReadOnlyDictionary<string, string> Format(NotificationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var args = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ActionKey] = Name(action.Kind),
            [ProfileKey] = action.ProfileId,
            [MedicineKey] = action.MedicineId.ToString("N"),
        };
        if (action.SlotTime is { } time) args[SlotKey] = time.ToString(TimeFormat, CultureInfo.InvariantCulture);
        return args;
    }

    // Null for arguments this version does not understand: the caller
    // then only brings the window forward.
    public static NotificationAction? Parse(IReadOnlyDictionary<string, string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!args.TryGetValue(ActionKey, out var name) || Kind(name) is not { } kind) return null;
        if (!args.TryGetValue(ProfileKey, out var profile) || string.IsNullOrWhiteSpace(profile)) return null;
        if (!args.TryGetValue(MedicineKey, out var medicine) || !Guid.TryParseExact(medicine, "N", out var medicineId))
            return null;
        TimeOnly? slot = null;
        if (args.TryGetValue(SlotKey, out var text))
        {
            if (!TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                return null;
            slot = t;
        }
        if (kind == NotificationActionKind.Snooze && slot is null) return null;
        return new NotificationAction(kind, profile, medicineId, slot);
    }

    private static string Name(NotificationActionKind kind) => kind switch
    {
        NotificationActionKind.Open => "open",
        NotificationActionKind.OpenPrescriptions => "prescriptions",
        NotificationActionKind.OpenDeadlines => "deadlines",
        NotificationActionKind.OpenPackages => "packages",
        NotificationActionKind.Snooze => "snooze",
        NotificationActionKind.RequestPrescription => "request",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static NotificationActionKind? Kind(string name) => name switch
    {
        "open" => NotificationActionKind.Open,
        "prescriptions" => NotificationActionKind.OpenPrescriptions,
        "deadlines" => NotificationActionKind.OpenDeadlines,
        "packages" => NotificationActionKind.OpenPackages,
        "snooze" => NotificationActionKind.Snooze,
        "request" => NotificationActionKind.RequestPrescription,
        _ => null,
    };
}
