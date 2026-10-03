using MedReminder.Domain.Ledger;

namespace MedReminder.Domain.Medicines;

// A time-of-day preset of the profile ("in the morning" = 08:00), used to
// place a slot without an explicit time in the day
// (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md §6). Display only: the
// time never changes stored stock, so editing it may apply to existing
// slots at once.
//
// Built-in presets live in code (BuiltInDoseTimePresets); a stored row
// with a BuiltInKey only overrides the time or hides it. A row without a
// BuiltInKey is a preset the user added, with its own label.
//
// Device-local: presets are not sync operations. A slot that refers to
// a user preset another device does not know has no time there.
public sealed class DoseTimePreset
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string? BuiltInKey { get; init; }

    // User presets only; built-ins are localized at display time.
    public string? Label { get; set; }

    // Null: no time of day, the dose stays in the end-of-day booking.
    public TimeOnly? Time { get; set; }

    // Default of the slot flag when the preset is picked (the flag itself
    // is stored on the slot, MedicationAdministrationSlot.IsAsNeeded).
    public bool IsAsNeeded { get; set; }

    public int Order { get; set; }

    // Built-ins cannot be deleted, only hidden from the slot dialog.
    public bool IsHidden { get; set; }
}

// The presets every profile starts with: key (also the suffix of the
// "Ui.AdministrationSlotDialog.Preset.<Key>" string), default time and
// as-needed default. Ids are name-based, so every device and every
// profile agree on them.
public static class BuiltInDoseTimePresets
{
    public sealed record Definition(string Key, TimeOnly? Time, bool IsAsNeeded = false)
    {
        public Guid Id => IdOf(Key);
    }

    public const string AsNeededKey = "AsNeeded";

    private static readonly Guid Namespace = Guid.Parse("6f1d4c2e-9a57-4b8e-8f0c-2d5e7a9b3c41");

    public static readonly IReadOnlyList<Definition> All =
    [
        new("Morning", new TimeOnly(8, 0)),
        new("MorningEmptyStomach", new TimeOnly(7, 30)),
        new("BeforeBreakfast", new TimeOnly(7, 30)),
        new("AfterBreakfast", new TimeOnly(8, 30)),
        new("MidMorning", new TimeOnly(10, 30)),
        new("BeforeLunch", new TimeOnly(13, 0)),
        new("AfterLunch", new TimeOnly(14, 0)),
        new("Afternoon", new TimeOnly(16, 0)),
        new("BeforeDinner", new TimeOnly(19, 30)),
        new("AfterDinner", new TimeOnly(20, 30)),
        new("BeforeSleep", new TimeOnly(22, 30)),
        new("Night", new TimeOnly(23, 30)),
        new(AsNeededKey, null, IsAsNeeded: true),
    ];

    public static Guid IdOf(string key) => DeterministicGuid.Create(Namespace, "dose-time-preset:" + key);

    public static Definition? Find(Guid id) => All.FirstOrDefault(d => d.Id == id);
}
