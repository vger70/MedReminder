using System.Globalization;

namespace MedReminder.Application.Notifications;

// What a Windows notification is about, so the toast can offer actions
// on it (docs/notes/EVOLUTION-PROPOSALS-2.md §3.4). Carries identifiers
// only: a toast's arguments are stored by Windows and must not hold a
// medicine name or any other health data.
public sealed record NotificationTarget(NotificationKind Kind, Guid MedicineId, TimeOnly? SlotTime = null)
{
    public static NotificationTarget DoseReminder(Guid medicineId, TimeOnly slotTime)
        => new(NotificationKind.DoseReminder, medicineId, slotTime);

    public static NotificationTarget LowStock(Guid medicineId) => new(NotificationKind.LowStock, medicineId);

    public static NotificationTarget Prescription(Guid medicineId) => new(NotificationKind.Prescription, medicineId);

    public static NotificationTarget Shortage(Guid medicineId) => new(NotificationKind.Shortage, medicineId);

    // Guid.Empty for a deadline of the profile.
    public static NotificationTarget Deadline(Guid? medicineId)
        => new(NotificationKind.Deadline, medicineId ?? Guid.Empty);

    public static NotificationTarget PackageExpiry(Guid medicineId) => new(NotificationKind.PackageExpiry, medicineId);
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
    // Body click on a prescription reminder: the prescriptions window.
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
