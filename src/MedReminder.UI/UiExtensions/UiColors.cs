namespace MedReminder.UI.UiExtensions;

// Text colours shared by the forms (docs/notes/EVOLUTION-PROPOSALS.md
// §3.2). A facade over UiTheme.Palette kept while the forms move to the
// theme tokens (docs/analysis/ANALYSIS-UI-MODERNIZATION.md §4.1). The
// palette is resolved on each access, so a high-contrast theme change
// applies to the next window or status update without a restart.
internal static class UiColors
{
    // Secondary text: hints, notes, neutral status.
    public static Color Hint => UiTheme.Palette.TextSecondary;

    public static Color Error => UiTheme.Palette.DangerText;

    public static Color Warning => UiTheme.Palette.WarningText;

    public static Color Success => UiTheme.Palette.OkText;

    public static bool HighContrast => UiTheme.HighContrast;
}
