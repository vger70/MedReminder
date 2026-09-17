using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace MedReminder.Infrastructure.Localization;

// ILocalizationService implementation (Increment 16a).
//
// Loading strategy:
//   1. For each supported language, try to read:
//      %LOCALAPPDATA%\MedReminder\localization\strings.<lang>.json
//      (optional user override). Corrupted file → silently ignored,
//      exception implicitly logged and swallowed.
//   2. Then read the embedded version from the assembly (resource
//      name: "MedReminder.Infrastructure.Localization.strings.<lang>.json"
//      — the UI project also includes it via Link with this
//      LogicalName, so loading works consistently from any
//      assembly).
//   3. The two maps are merged: user overrides win over embedded
//      values for the same key.
//
// The maps are cached for the entire process lifetime (registered as
// Singleton). A language change requires a restart — consistent
// with the user documentation.
public sealed class LocalizationService : ILocalizationService
{
    private const string OverrideSubdirectory = "localization";
    // Suffix searched inside embedded-resource names — avoids
    // depending on the exact format chosen by MSBuild (RootNamespace
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

    // Factory used by Program.Main to create an instance BEFORE the
    // IHost / DI are ready — so pre-boot messages (single-instance
    // mutex, ThreadException) are localizable too. Reads
    // user.settings.json by hand; if the file is missing or
    // corrupted, falls back to the default language (en).
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

        // 1. Look up in the requested language.
        if (_dictionaries.TryGetValue(target.Code, out var primary)
            && primary.TryGetValue(key, out var value))
        {
            return FormatArgs(value, target.Culture, args);
        }

        // 2. Fallback to the default language.
        if (!string.Equals(target.Code, SupportedLanguages.Default, StringComparison.OrdinalIgnoreCase)
            && _dictionaries.TryGetValue(SupportedLanguages.Default, out var fallback)
            && fallback.TryGetValue(key, out var fallbackValue))
        {
            return FormatArgs(fallbackValue, target.Culture, args);
        }

        // 3. Final fallback: the key itself wrapped in square
        // brackets — visible in the UI, helps the developer find
        // gaps.
        return "[" + key + "]";
    }

    private static string FormatArgs(string template, CultureInfo culture, object?[] args)
    {
        if (args is null || args.Length == 0) return template;
        try { return string.Format(culture, template, args); }
        catch (FormatException)
        {
            // If the template has placeholders that are incompatible
            // with the args, do not crash the UI: return the raw
            // string as graceful degradation.
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
            // Precedence order (strongest to weakest):
            //   1. user override in %LOCALAPPDATA%\MedReminder\
            //      localization\strings.<lang>.json
            //   2. "distributed" files copied into
            //      <bin>\localization\strings.<lang>.json (csproj
            //      Content)
            //   3. embedded resource (for single-file publish or
            //      safety)
            //
            // Later maps are MERGED — user overrides win over the
            // defaults, but keys missing from the override fall
            // back to the defaults.
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
        // Robust lookup: scans EVERY loaded assembly and finds the
        // one that embeds a resource whose name ends with
        // "strings.<lang>.json". This avoids any assumption about
        // the exact ManifestResourceName (LogicalName vs
        // RootNamespace+path are both valid across different
        // SDK-style projects).
        var suffix = string.Format(CultureInfo.InvariantCulture, ResourceSuffixFormat, languageCode);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string[] resourceNames;
            try { resourceNames = assembly.GetManifestResourceNames(); }
            catch { continue; }  // dynamic assemblies reject GetManifestResourceNames

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
                    // Corrupted JSON file — try the next resource, if
                    // there is more than one.
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
            // Corrupted user file: silent, the app uses the
            // embedded strings.
            return EmptyDictionary;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyDictionary =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
