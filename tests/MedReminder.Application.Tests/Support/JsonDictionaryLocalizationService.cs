using System.Globalization;
using System.Text.Json;
using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Tests.Support;

// Test double for ILocalizationService backed by the shipped
// dictionaries copied next to the test assembly (see the csproj
// Content items). Mirrors the production fallback rules: requested
// language, then English, then "[key]" so a missing key is visible in
// the rendered text.
internal sealed class JsonDictionaryLocalizationService : ILocalizationService
{
    private readonly Dictionary<string, string> _current;
    private readonly Dictionary<string, string> _fallback;

    public JsonDictionaryLocalizationService(string languageCode)
    {
        CurrentLanguage = languageCode;
        CurrentCulture = CultureInfo.GetCultureInfo(languageCode);
        _current = Load(languageCode);
        _fallback = Load(SupportedLanguages.Default);
    }

    public string CurrentLanguage { get; }

    public CultureInfo CurrentCulture { get; }

    // Keys resolved only through the English fallback: lets a test
    // assert that a language defines every key it renders.
    public List<string> FallbackHits { get; } = new();

    public string Get(string key, params object?[] args) => Format(Lookup(key), args);

    public string GetIn(string languageCode, string key, params object?[] args)
        => new JsonDictionaryLocalizationService(languageCode).Get(key, args);

    private string Lookup(string key)
    {
        if (_current.TryGetValue(key, out var value)) return value;
        FallbackHits.Add(key);
        return _fallback.TryGetValue(key, out var en) ? en : $"[{key}]";
    }

    private string Format(string template, object?[] args)
        => args.Length == 0 ? template : string.Format(CurrentCulture, template, args);

    private static Dictionary<string, string> Load(string languageCode)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "localization", $"strings.{languageCode}.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Empty dictionary: {path}");
    }
}
