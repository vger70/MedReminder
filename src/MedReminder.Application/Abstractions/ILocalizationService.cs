using System.Globalization;

namespace MedReminder.Application.Abstractions;

// Localization service for UI strings, email bodies and reports
// (Increment 16). Strings are dotted keys with a namespace
// (e.g. "Ui.MainForm.Menu.File", "Notifications.Email.Subject") and
// live in per-language JSON files embedded in the UI assembly, with
// an optional on-disk override for advanced user customization.
//
// Fallback rules:
//   1. Look up the key in the requested language's dictionary.
//   2. If missing, look it up in the default language
//      (SupportedLanguages.Default = "en").
//   3. If still missing, return the key itself wrapped in square
//      brackets (e.g. "[Ui.MainForm.Menu.File]") — visible in the UI
//      so the developer immediately spots the gap. Never throws.
public interface ILocalizationService
{
    // Language currently selected by the user (from user.settings.
    // json). Does not change at runtime — after a save, the change
    // takes effect on restart.
    string CurrentLanguage { get; }

    // Translates the key in the current language. Params are passed
    // to string.Format with the CultureInfo of the current language.
    string Get(string key, params object?[] args);

    // Overload that forces a specific language. Used by
    // MedicationMonitor:
    //   - email → GetIn(userLang, key)   (user's chosen language)
    //   - toast → GetIn(systemLang, key) (Windows system language)
    string GetIn(string languageCode, string key, params object?[] args);

    // CultureInfo of the current language. Used to format numbers
    // and dates when consistency with the translated text matters.
    CultureInfo CurrentCulture { get; }
}
