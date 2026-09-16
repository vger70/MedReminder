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
public sealed class LocalizationService : ILocalizationService
{
    private const string OverrideSubdirectory = "localization";
    // Suffix cercato dentro i nomi delle resource embedded — evita
    // di dipendere dal formato esatto scelto da MSBuild (RootNamespace
    // + path vs LogicalName override).
    private const string ResourceSuffixFormat = "strings.{0}.json";

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _dictionaries;
    private readonly SupportedLanguage _current;

    public LocalizationService(IOptions<UserSettings> settings)
        : this(settings.Value.Language)
    { }

    private LocalizationService(string languageCode)
    {
        _dictionaries = LoadAllDictionaries();
        _current = SupportedLanguages.Resolve(languageCode);
    }

    // Factory usata da Program.Main per creare un'istanza PRIMA che
    // l'IHost/DI siano pronti — così i messaggi pre-boot (mutex
    // single-instance, ThreadException) sono localizzabili anch'essi.
    // Legge user.settings.json a mano; se il file manca o è corrotto
    // ricade sulla lingua di default (en).
    public static ILocalizationService CreateStandalone(string? languageCode)
        => new LocalizationService(languageCode ?? SupportedLanguages.Default);

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
            // Ordine di precedenza (dal più forte al più debole):
            //   1. override utente in %LOCALAPPDATA%\MedReminder\
            //      localization\strings.<lang>.json
            //   2. file "distribuiti" copiati in <bin>\localization\
            //      strings.<lang>.json (Content del csproj)
            //   3. resource embedded (per single-file publish o safety)
            //
            // Le mappe successive vengono FUSE — le override utente
            // vincono sui default, ma le chiavi mancanti nell'override
            // ricadono sulle default.
            var overrides = LoadOverride(lang.Code);
            var baseDir = LoadFromBaseDirectory(lang.Code);
            var embedded = LoadEmbedded(lang.Code);

            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in embedded) merged[kv.Key] = kv.Value;
            foreach (var kv in baseDir) merged[kv.Key] = kv.Value;
            foreach (var kv in overrides) merged[kv.Key] = kv.Value;
            result[lang.Code] = merged;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> LoadFromBaseDirectory(string languageCode)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, OverrideSubdirectory,
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
            return EmptyDictionary;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadEmbedded(string languageCode)
    {
        // Ricerca robusta: scansiona TUTTI gli assembly caricati e trova
        // quello che embed una resource il cui nome finisce in
        // "strings.<lang>.json". In questo modo non ci sono ipotesi sul
        // ManifestResourceName esatto (LogicalName vs RootNamespace+path
        // sono entrambi validi in progetti SDK-style diversi).
        var suffix = string.Format(CultureInfo.InvariantCulture, ResourceSuffixFormat, languageCode);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string[] resourceNames;
            try { resourceNames = assembly.GetManifestResourceNames(); }
            catch { continue; }  // dynamic assemblies rifiutano GetManifestResourceNames

            foreach (var name in resourceNames)
            {
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null) continue;
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                    if (dict is not null)
                    {
                        return new Dictionary<string, string>(dict, StringComparer.Ordinal);
                    }
                }
                catch (JsonException)
                {
                    // file JSON corrotto — proviamo la prossima resource,
                    // se ce n'è più di una.
                }
            }
        }
        return EmptyDictionary;
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

    private static readonly IReadOnlyDictionary<string, string> EmptyDictionary =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
