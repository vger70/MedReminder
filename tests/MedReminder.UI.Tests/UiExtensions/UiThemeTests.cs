using System.Drawing;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.UI.UiExtensions;
using Xunit;

namespace MedReminder.UI.Tests.UiExtensions;

// Guards the palette contract of docs/analysis/ANALYSIS-UI-MODERNIZATION.md
// §4.1: every text/background pair of the light and dark palettes reads
// at 4.5:1 or more (WCAG 2.x, AA for normal text).
public sealed class UiThemeTests
{
    public static TheoryData<string> Palettes => new() { "Light", "Dark" };

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Every_text_pair_meets_AA_contrast(string name)
    {
        var p = name == "Light" ? UiTheme.Light : UiTheme.Dark;
        var pairs = new (string Pair, Color Fore, Color Back)[]
        {
            ("Text/Surface", p.Text, p.Surface),
            ("Text/Background", p.Text, p.Background),
            ("Text/Hover", p.Text, p.Hover),
            ("TextSecondary/Surface", p.TextSecondary, p.Surface),
            ("TextSecondary/Background", p.TextSecondary, p.Background),
            ("Accent/Surface", p.Accent, p.Surface),
            ("OnAccent/Accent", p.OnAccent, p.Accent),
            ("OnAccent/AccentHover", p.OnAccent, p.AccentHover),
            ("SelectionText/Selection", p.SelectionText, p.Selection),
            ("Ok", p.OkText, p.OkBack),
            ("Warning", p.WarningText, p.WarningBack),
            ("Danger", p.DangerText, p.DangerBack),
            ("Neutral", p.NeutralText, p.NeutralBack),
        };

        foreach (var (pair, fore, back) in pairs)
        {
            ContrastRatio(fore, back).Should().BeGreaterThanOrEqualTo(4.5, $"{name} {pair} must meet WCAG AA");
        }
    }

    [Theory]
    [InlineData(AppearanceMode.System, SystemColorMode.System)]
    [InlineData(AppearanceMode.Light, SystemColorMode.Classic)]
    [InlineData(AppearanceMode.Dark, SystemColorMode.Dark)]
    public void Appearance_maps_to_the_WinForms_color_mode(AppearanceMode mode, SystemColorMode expected)
        => UiTheme.ToColorMode(mode).Should().Be(expected);

    private static double ContrastRatio(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
