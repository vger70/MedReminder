using System.Globalization;

namespace MedReminder.Application.Abstractions;

// Non-SMTP, non-Backup user preferences that live in
// %LOCALAPPDATA%\MedReminder\user.settings.json.
// Currently only holds the language (Increment 16 — localization).
// Same pattern as SmtpSettings / BackupSettings: "UI" section in
// IConfiguration, editable POCO, IOptionsMonitor for hot reload
// (even though the language change still requires a restart, the
// reload lets the new process pick it up immediately).
public sealed class UserSettings
{
    public const string SectionName = "UI";

    // ISO 639-1 language code, lowercase. Supported values:
    //   "en" — English (default)
    //   "it" — Italian
    //   "fr" — French
    //   "es" — Spanish
    //   "de" — German
    // Any other value falls back to English (fail-safe).
    public string Language { get; set; } = "en";

    // Reference-catalogue country used by the medicine autocomplete.
    // ISO 3166-1 alpha-2 (uppercase) or "EU". Default "IT" matches
    // the AIFA snapshot shipped with the app. See
    // docs/ANALYSIS-DRUG-CATALOGUE.md §2.2 for the country-scope rule.
    public string ReferenceCountry { get; set; } = "IT";

    // Passive update check: on startup, query the GitHub Releases
    // API for the latest published release and, if the tag is newer
    // than the running assembly version, show a non-modal
    // notification pointing at the release page. No download and no
    // in-place update — the user still installs the new build
    // manually. Defaults to true; the user can opt out from the
    // General tab of the Settings dialog. The manual "Check for
    // updates now" menu entry always runs regardless of this flag.
    public bool CheckForUpdatesOnStartup { get; set; } = true;
}

// Definition of the "currently supported" languages — used by the UI
// and by LocalizationService to validate the input.
public static class SupportedLanguages
{
    public const string Default = "en";

    public static readonly IReadOnlyList<SupportedLanguage> All = new[]
    {
        new SupportedLanguage("en", "English", CultureInfo.GetCultureInfo("en")),
        new SupportedLanguage("it", "Italiano", CultureInfo.GetCultureInfo("it")),
        new SupportedLanguage("fr", "Français", CultureInfo.GetCultureInfo("fr")),
        new SupportedLanguage("es", "Español", CultureInfo.GetCultureInfo("es")),
        new SupportedLanguage("de", "Deutsch", CultureInfo.GetCultureInfo("de")),
    };

    public static SupportedLanguage Resolve(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return All[0];
        foreach (var l in All)
        {
            if (string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return l;
            }
        }
        return All[0];
    }
}

public sealed record SupportedLanguage(string Code, string DisplayName, CultureInfo Culture);
