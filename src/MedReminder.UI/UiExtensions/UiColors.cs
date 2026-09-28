namespace MedReminder.UI.UiExtensions;

// Text colours shared by the forms (docs/notes/EVOLUTION-PROPOSALS.md
// §3.2). Under a Windows high-contrast theme the fixed colours are
// replaced by the theme's own, which the user picked to be legible;
// the check runs on each access, so a theme change applies to the
// next window or status update without a restart.
internal static class UiColors
{
    // Secondary text: hints, notes, neutral status. GrayText follows
    // the theme and, on the default theme, reads at about 4.6:1 on the
    // dialog background, where the former DarkGray read at about 2:1.
    public static Color Hint => SystemColors.GrayText;

    public static Color Error => Themed(Color.Firebrick);

    public static Color Warning => Themed(Color.DarkOrange);

    public static Color Success => Themed(Color.DarkGreen);

    public static bool HighContrast => SystemInformation.HighContrast;

    // `normal` on the default theme, the theme's text colour under
    // high contrast.
    public static Color Themed(Color normal)
        => SystemInformation.HighContrast ? SystemColors.ControlText : normal;
}
