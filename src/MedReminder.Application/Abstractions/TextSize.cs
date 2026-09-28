namespace MedReminder.Application.Abstractions;

// Accessible text size of the UI (docs/notes/EVOLUTION-PROPOSALS.md
// §3.2). A per-profile preference, stored in
// profiles\<id>\ui.settings.json, so an elderly user and a caregiver
// who share the PC can each keep their own size. Applied when a window
// loads; windows shown before a profile is chosen (picker, PIN, first
// run wizard) use Normal.
public enum TextSize
{
    Normal = 0,
    Large = 1,
    ExtraLarge = 2,
}

public static class TextSizes
{
    // Scale factor applied to fonts, control bounds, grid rows and
    // list columns. Normal is 1 so the default look is unchanged.
    public static float ScaleOf(TextSize size) => size switch
    {
        TextSize.Large => 1.25f,
        TextSize.ExtraLarge => 1.5f,
        _ => 1f,
    };

    // Case-insensitive name; a missing or unknown value is Normal
    // (fail-safe: a damaged file must not block the UI).
    public static TextSize Parse(string? value)
        => Enum.TryParse<TextSize>(value, ignoreCase: true, out var size)
           && Enum.IsDefined(size)
           && !int.TryParse(value, out _)
            ? size
            : TextSize.Normal;
}
