using System.Drawing.Text;
using MedReminder.Application.Abstractions;
using WinFormsApp = System.Windows.Forms.Application;

namespace MedReminder.UI.UiExtensions;

// Colours of one appearance (docs/analysis/ANALYSIS-UI-MODERNIZATION.md
// §4.1). Every text/background pair reads at 4.5:1 or more (WCAG AA).
internal sealed record UiPalette(
    Color Background,
    Color Surface,
    Color Border,
    Color Hover,
    Color Text,
    Color TextSecondary,
    Color Accent,
    Color OnAccent,
    Color AccentHover,
    Color Selection,
    Color SelectionText,
    Color OkText,
    Color OkBack,
    Color WarningText,
    Color WarningBack,
    Color DangerText,
    Color DangerBack,
    Color NeutralText,
    Color NeutralBack);

// Single source of the UI's colours, fonts and spacing. The palette is
// resolved on each access, like UiColors before it: a Windows
// high-contrast theme maps every token to a system colour, otherwise
// the light or dark palette follows Application.IsDarkModeEnabled,
// which Program sets from the profile's Appearance preference.
internal static class UiTheme
{
    // Background and Surface match the WinForms dark system colours
    // (Control #202020, Window #323232), so stock controls and themed
    // ones sit on the same greys.
    public static readonly UiPalette Light = new(
        Background: Hex(0xF3F3F3),
        Surface: Hex(0xFFFFFF),
        Border: Hex(0xE0E0E0),
        Hover: Hex(0xEAEAEA),
        Text: Hex(0x1B1B1B),
        TextSecondary: Hex(0x5C5C5C),
        Accent: Hex(0x0F6CBD),
        OnAccent: Hex(0xFFFFFF),
        AccentHover: Hex(0x115EA3),
        Selection: Hex(0xCFE4FA),
        SelectionText: Hex(0x1B1B1B),
        OkText: Hex(0x0E700E),
        OkBack: Hex(0xDFF6DD),
        WarningText: Hex(0x835B00),
        WarningBack: Hex(0xFFF4CE),
        DangerText: Hex(0xB10E1C),
        DangerBack: Hex(0xFDE7E9),
        NeutralText: Hex(0x484644),
        NeutralBack: Hex(0xEDEBE9));

    public static readonly UiPalette Dark = new(
        Background: Hex(0x202020),
        Surface: Hex(0x323232),
        Border: Hex(0x464646),
        Hover: Hex(0x3D3D3D),
        Text: Hex(0xFFFFFF),
        TextSecondary: Hex(0xC5C5C5),
        Accent: Hex(0x62ABF5),
        OnAccent: Hex(0x000000),
        AccentHover: Hex(0x7DB9F7),
        Selection: Hex(0x0E4775),
        SelectionText: Hex(0xFFFFFF),
        OkText: Hex(0x9FD89F),
        OkBack: Hex(0x1E3A1E),
        WarningText: Hex(0xF4D38A),
        WarningBack: Hex(0x3D300E),
        DangerText: Hex(0xF1BBBC),
        DangerBack: Hex(0x4A2426),
        NeutralText: Hex(0xC8C8C8),
        NeutralBack: Hex(0x404040));

    // Appearance chosen for the open profile; set once at boot.
    public static AppearanceMode Appearance { get; set; } = AppearanceMode.System;

    public static bool HighContrast => SystemInformation.HighContrast;

    public static bool IsDark => !HighContrast && WinFormsApp.IsDarkModeEnabled;

    public static UiPalette Palette => HighContrast ? HighContrastPalette() : IsDark ? Dark : Light;

    // Windows owns the colours under high contrast: the user picked
    // them to be legible, so status is told by text, not by colour.
    private static UiPalette HighContrastPalette() => new(
        Background: SystemColors.Control,
        Surface: SystemColors.Window,
        Border: SystemColors.WindowFrame,
        Hover: SystemColors.Highlight,
        Text: SystemColors.WindowText,
        TextSecondary: SystemColors.GrayText,
        Accent: SystemColors.Highlight,
        OnAccent: SystemColors.HighlightText,
        AccentHover: SystemColors.Highlight,
        Selection: SystemColors.Highlight,
        SelectionText: SystemColors.HighlightText,
        OkText: SystemColors.WindowText,
        OkBack: SystemColors.Window,
        WarningText: SystemColors.WindowText,
        WarningBack: SystemColors.Window,
        DangerText: SystemColors.WindowText,
        DangerBack: SystemColors.Window,
        NeutralText: SystemColors.WindowText,
        NeutralBack: SystemColors.Window);

    // Classic is WinForms' name for the light look. System follows the
    // Windows "app mode" setting, which WinForms reads on Windows 11
    // only; on Windows 10 it stays light.
    public static SystemColorMode ToColorMode(AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => SystemColorMode.Classic,
        AppearanceMode.Dark => SystemColorMode.Dark,
        _ => SystemColorMode.System,
    };

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    // Type scale in points at text size Normal (§4.2). Segoe UI
    // Variable ships with Windows 11; Windows 10 falls back to Segoe UI.
    internal static class Fonts
    {
        public const float BodySize = 10f;
        public const float CaptionSize = 9f;
        public const float HeadingSize = 12f;
        public const float TitleSize = 16f;

        private static readonly Lazy<HashSet<string>> Installed = new(() =>
        {
            using var collection = new InstalledFontCollection();
            return collection.Families
                .Select(f => f.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        });

        public static string BodyFamily => FirstInstalled("Segoe UI Variable Text", "Segoe UI");

        public static string DisplayFamily => FirstInstalled("Segoe UI Variable Display Semibold", "Segoe UI Semibold", "Segoe UI");

        public static string MonoFamily => FirstInstalled("Cascadia Mono", "Consolas");

        // Static Segoe UI for date pickers, which cut the first digit
        // with the variable fonts (UiThemeApplier).
        public const string DatePickerFamily = "Segoe UI";

        // Fonts are created on each call: the caller owns the instance,
        // as with the `new Font(...)` calls this replaces.
        public static Font Body() => new(BodyFamily, BodySize);

        public static Font Caption() => new(BodyFamily, CaptionSize);

        public static Font Heading() => new(DisplayFamily, HeadingSize);

        public static Font Title() => new(DisplayFamily, TitleSize);

        public static Font Mono() => new(MonoFamily, BodySize);

        public static bool IsInstalled(string family) => Installed.Value.Contains(family);

        private static string FirstInstalled(params string[] families)
            => families.FirstOrDefault(IsInstalled) ?? families[^1];
    }

    // Spacing scale in pixels at 96 DPI (§4.3); MedReminderFormBase
    // scales it with the display and the text size.
    internal static class Space
    {
        public const int XS = 4;
        public const int S = 8;
        public const int M = 12;
        public const int L = 16;
        public const int XL = 24;
        public const int XXL = 32;

        public const int ButtonHeight = 32;
        public const int ButtonMinWidth = 96;
    }
}
