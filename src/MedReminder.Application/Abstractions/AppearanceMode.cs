namespace MedReminder.Application.Abstractions;

// Light or dark look of the UI (docs/analysis/ANALYSIS-UI-MODERNIZATION.md
// §4.4). A per-profile preference stored next to the text size in
// profiles\<id>\ui.settings.json; like the text size it belongs to one
// device and is not replicated by the household sync. Applied at boot;
// windows shown before a profile is chosen follow Windows.
public enum AppearanceMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public static class AppearanceModes
{
    // Case-insensitive name; a missing or unknown value is System
    // (fail-safe: a damaged file must not block the UI).
    public static AppearanceMode Parse(string? value)
        => Enum.TryParse<AppearanceMode>(value, ignoreCase: true, out var mode)
           && Enum.IsDefined(mode)
           && !int.TryParse(value, out _)
            ? mode
            : AppearanceMode.System;
}
