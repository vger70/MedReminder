using System.Globalization;

namespace MedReminder.Application.Abstractions;

// Preferenze utente non-SMTP e non-Backup che vivono in
// %LOCALAPPDATA%\MedReminder\user.settings.json.
// Al momento contiene solo la lingua (Incremento 16 — localizzazione).
// Pattern coerente con SmtpSettings/BackupSettings: sezione "UI"
// nell'IConfiguration, POCO editabile, IOptionsMonitor per il reload
// a caldo (anche se la lingua richiede comunque restart, il reload
// permette al nuovo processo di leggerla immediatamente).
public sealed class UserSettings
{
    public const string SectionName = "UI";

    // Codice lingua ISO 639-1, lowercase. Valori supportati:
    //   "en" — inglese (default)
    //   "it" — italiano
    // Ogni altro valore ricade sull'inglese (fail-safe).
    public string Language { get; set; } = "en";
}

// Definizione della lingua "attualmente supportata" — usata da UI e
// LocalizationService per validare l'input.
public static class SupportedLanguages
{
    public const string Default = "en";

    public static readonly IReadOnlyList<SupportedLanguage> All = new[]
    {
        new SupportedLanguage("en", "English", CultureInfo.GetCultureInfo("en")),
        new SupportedLanguage("it", "Italiano", CultureInfo.GetCultureInfo("it")),
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
