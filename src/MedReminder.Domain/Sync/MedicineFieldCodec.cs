using System.Globalization;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;

namespace MedReminder.Domain.Sync;

// The replicated scalar fields of a medicine (docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §4.2, "Mutable record"), each with a
// culture-invariant text form. Operations carry field values as text, so
// last-writer-wins per field needs one rule for every field.
//
// Not listed, and why: IsActive (activity history), StartDate
// (immutable), DosePerAdministration and AdministrationsPerDay (derived
// from the schedule history), StockEpoch, LedgerBaselineEpoch and
// StockEpochFactId (derived), CreatedAt and UpdatedAt (local metadata).
//
// Field names are part of the sync format: never rename one.
public static class MedicineFieldCodec
{
    private static readonly Field[] All =
    [
        new("Name", m => m.Name, (m, v) => m.Name = v ?? throw new FormatException("Name cannot be null.")),
        new("ActiveIngredient", m => m.ActiveIngredient, (m, v) => m.ActiveIngredient = v),
        new("Package", m => m.Package, (m, v) => m.Package = v),
        new("Unit", m => m.Unit, (m, v) => m.Unit = v ?? throw new FormatException("Unit cannot be null.")),
        new("ThresholdDays",
            m => m.ThresholdDays.ToString(CultureInfo.InvariantCulture),
            (m, v) => m.ThresholdDays = int.Parse(Required(v), NumberStyles.Integer, CultureInfo.InvariantCulture)),
        new("DoctorName", m => m.DoctorName, (m, v) => m.DoctorName = v),
        new("Notes", m => m.Notes, (m, v) => m.Notes = v),
        new("NotificationChannels",
            m => ((int)m.NotificationChannels).ToString(CultureInfo.InvariantCulture),
            (m, v) => m.NotificationChannels = (NotificationChannels)int.Parse(
                Required(v), NumberStyles.Integer, CultureInfo.InvariantCulture)),
        new("RemindOnDose",
            m => m.RemindOnDose ? "true" : "false",
            (m, v) => m.RemindOnDose = Required(v) switch
            {
                "true" => true,
                "false" => false,
                _ => throw new FormatException($"Invalid boolean '{v}'."),
            }),
        new("NationalCode", m => m.NationalCode, (m, v) => m.NationalCode = v),
        new("AtcCode", m => m.AtcCode?.Value, (m, v) => m.AtcCode = v is null ? null : AtcCode.Parse(v)),
        new("LinkedReferenceMedicineId",
            m => m.LinkedReferenceMedicineId?.ToString("D"),
            (m, v) => m.LinkedReferenceMedicineId = v is null ? null : Guid.ParseExact(v, "D")),
        new("EndDate",
            m => m.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            (m, v) => m.EndDate = v is null
                ? null
                : DateOnly.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
    ];

    private static readonly Dictionary<string, Field> ByName =
        All.ToDictionary(f => f.Name, StringComparer.Ordinal);

    public static IReadOnlyList<string> FieldNames { get; } = [.. All.Select(f => f.Name)];

    // Every replicated field of `medicine`, in declaration order.
    public static IReadOnlyList<MedicineFieldValue> Snapshot(Medicine medicine)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        return [.. All.Select(f => new MedicineFieldValue(f.Name, f.Get(medicine)))];
    }

    // The fields whose value differs between the two medicines, with
    // the value of `after`, in declaration order.
    public static IReadOnlyList<MedicineFieldValue> Diff(
        IReadOnlyList<MedicineFieldValue> before, Medicine after)
    {
        ArgumentNullException.ThrowIfNull(before);
        var old = before.ToDictionary(v => v.Field, v => v.Value, StringComparer.Ordinal);
        return [.. Snapshot(after).Where(v => !old.TryGetValue(v.Field, out var o) || !string.Equals(o, v.Value, StringComparison.Ordinal))];
    }

    public static string? Get(Medicine medicine, string field)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        return Lookup(field).Get(medicine);
    }

    public static void Set(Medicine medicine, string field, string? value)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        Lookup(field).Set(medicine, value);
    }

    private static Field Lookup(string field)
        => ByName.TryGetValue(field, out var f)
            ? f
            : throw new ArgumentException($"Unknown medicine field '{field}'.", nameof(field));

    private static string Required(string? value)
        => value ?? throw new FormatException("Value cannot be null.");

    private sealed record Field(string Name, Func<Medicine, string?> Get, Action<Medicine, string?> Set);
}
