using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace MedReminder.Infrastructure.Localization;

// Implementazione ILocalizationService (Incremento 16a).
//
// Strategia di caricamento:
//   1. Per ogni lingua supportata, tenta di leggere:
//      %LOCALAPPDATA%\MedReminder\localization\strings.<lang>.json
//      (override utente, opzionale). File corrotto → viene ignorato,
//      log warning implicito con l'exception silenziata.
//   2. Poi legge la versione embedded da assembly (nome resource:
//      "MedReminder.Infrastructure.Localization.strings.<lang>.json"
//      — il progetto UI la include tramite Link con questo
//      LogicalName, così il caricamento è consistente da qualunque
//      assembly).
//   3. Le due mappe sono fuse: le override utente vincono sui
//      valori embedded per la stessa chiave.
//
// Le mappe sono cache-ate per l'intera vita del processo (registrato
// Singleton). Il cambio lingua richiede restart — coerente con la
// documentazione utente.
internal sealed class LocalizationService : ILocalizationService
{
    private const string OverrideSubdirectory = "localization";
    private const string ResourceNameFormat = "MedReminder.UI.strings.{0}.json";

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _dictionaries;
    private readonly SupportedLanguage _current;

    public LocalizationService(IOptions<UserSettings> settings)
    {
        _dictionaries = LoadAllDictionaries();
        _current = SupportedLanguages.Resolve(settings.Value.Language);
    }

    public string CurrentLanguage => _current.Code;

    public CultureInfo CurrentCulture => _current.Culture;

    public string Get(string key, params object?[] args)
        => GetIn(_current.Code, key, args);

    public string GetIn(string languageCode, string key, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var target = SupportedLanguages.Resolve(languageCode);

        // 1. Cerca nella lingua richiesta.
        if (_dictionaries.TryGetValue(target.Code, out var primary)
            && primary.TryGetValue(key, out var value))
        {
            return FormatArgs(value, target.Culture, args);
        }

        // 2. Fallback su lingua default.
        if (!string.Equals(target.Code, SupportedLanguages.Default, StringComparison.OrdinalIgnoreCase)
            && _dictionaries.TryGetValue(SupportedLanguages.Default, out var fallback)
            && fallback.TryGetValue(key, out var fallbackValue))
        {
            return FormatArgs(fallbackValue, target.Culture, args);
        }

        // 3. Fallback finale: la chiave stessa fra parentesi quadre —
        // visibile in UI, aiuta il developer a trovare le lacune.
        return "[" + key + "]";
    }

    private static string FormatArgs(string template, CultureInfo culture, object?[] args)
    {
        if (args is null || args.Length == 0) return template;
        try { return string.Format(culture, template, args); }
        catch (FormatException)
        {
            // Se il template ha placeholder non compatibili con gli args
            // passati, non fare crashare l'UI: ritorna la stringa
            // grezza come degradation graziosa.
            return template;
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
        LoadAllDictionaries()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var lang in SupportedLanguages.All)
        {
            var embedded = LoadEmbedded(lang.Code);
            var overrides = LoadOverride(lang.Code);
            if (overrides.Count == 0)
            {
                result[lang.Code] = embedded;
                continue;
            }
            var merged = new Dictionary<string, string>(embedded, StringComparer.Ordinal);
            foreach (var kv in overrides) merged[kv.Key] = kv.Value;
            result[lang.Code] = merged;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> LoadEmbedded(string languageCode)
    {
        // La resource è embedded nel progetto UI con LogicalName
        // "MedReminder.UI.strings.<lang>.json" (vedi
        // MedReminder.UI.csproj). Il caricamento cerca l'assembly UI
        // per name — evita accoppiamento hard tra Infrastructure e UI
        // via ProjectReference.
        var name = string.Format(CultureInfo.InvariantCulture, ResourceNameFormat, languageCode);

        var uiAssembly = FindUiAssembly();
        if (uiAssembly is null) return EmptyDictionary;

        using var stream = uiAssembly.GetManifestResourceStream(name);
        if (stream is null) return EmptyDictionary;

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            return dict is not null
                ? new Dictionary<string, string>(dict, StringComparer.Ordinal)
                : EmptyDictionary;
        }
        catch (JsonException)
        {
            return EmptyDictionary;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadOverride(string languageCode)
    {
        var path = Path.Combine(
            AppDataPaths.GetAppDataDirectory(),
            OverrideSubdirectory,
            $"strings.{languageCode}.json");
        if (!File.Exists(path)) return EmptyDictionary;
        try
        {
            using var stream = File.OpenRead(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            return dict is not null
                ? new Dictionary<string, string>(dict, StringComparer.Ordinal)
                : EmptyDictionary;
        }
        catch
        {
            // File utente corrotto: silenzioso, l'app usa gli embedded.
            return EmptyDictionary;
        }
    }

    // Cerca l'assembly UI per nome — evita che Infrastructure abbia
    // una ProjectReference verso UI (che è la sua consumer).
    private static Assembly? FindUiAssembly()
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = a.GetName().Name;
            if (string.Equals(name, "MedReminder", StringComparison.Ordinal) ||
                string.Equals(name, "MedReminder.UI", StringComparison.Ordinal))
            {
                return a;
            }
        }
        return null;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyDictionary =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
