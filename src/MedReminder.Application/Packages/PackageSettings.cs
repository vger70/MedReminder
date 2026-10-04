using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Packages;

// The lead-day profile settings of the package expiry (docs/analysis/
// ANALYSIS-PACKAGE-EXPIRY.md §4.6): invariant integers in the
// replicated settings, "" (or a value this build cannot read) for the
// default, a value out of range clamped.
public static class PackageSettings
{
    public static PackageLeadDays LeadDays(IReadOnlyDictionary<string, string?> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return LeadDays(settings.GetValueOrDefault(ProfileSetting.PackageExpiryLeadDays),
            settings.GetValueOrDefault(ProfileSetting.PackageInUseLeadDays));
    }

    public static PackageLeadDays LeadDays(string? printed, string? inUse)
        => new PackageLeadDays(Parse(printed, PackageLeadDays.DefaultPrinted), Parse(inUse, PackageLeadDays.DefaultInUse))
            .Clamped();

    // The defaults without a settings store (tests, tools).
    public static PackageLeadDays LeadDays(IProfileSettingsStore? store)
        => store is null ? PackageLeadDays.Default : LeadDays(store.Read());

    // The stored form: "" for the default, so a profile that never chose
    // a value follows a later change of the default.
    public static string FormatPrinted(int days) => Format(days, PackageLeadDays.DefaultPrinted);

    public static string FormatInUse(int days) => Format(days, PackageLeadDays.DefaultInUse);

    // Whether a value can be stored: "" or an integer from 0 to max.
    public static bool IsValidPrinted(string? value) => IsValid(value, PackageLeadDays.MaxPrinted);

    public static bool IsValidInUse(string? value) => IsValid(value, PackageLeadDays.MaxInUse);

    private static string Format(int days, int defaultDays)
        => days == defaultDays ? string.Empty : days.ToString(CultureInfo.InvariantCulture);

    private static bool IsValid(string? value, int max)
        => string.IsNullOrWhiteSpace(value)
            || (int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days <= max);

    private static int Parse(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) ? days : fallback;
}
