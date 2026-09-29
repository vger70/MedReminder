using System.Globalization;
using System.Text.Json;
using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Tests.Support;

// ILocalizationService backed by the shipped JSON dictionaries copied
// to the test output folder. Invariant culture keeps the formatted
// dates and numbers independent of the build machine.
internal sealed class DictionaryLocalizationService : ILocalizationService
{
    private readonly Dictionary<string, string> _strings;

    public DictionaryLocalizationService(string languageCode = "en")
    {
        CurrentLanguage = languageCode;
        _strings = Load(languageCode);
    }

    public string CurrentLanguage { get; }

    public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

    public string Get(string key, params object?[] args)
        => _strings.TryGetValue(key, out var value)
            ? string.Format(CurrentCulture, value, args)
            : $"[{key}]";

    public string GetIn(string languageCode, string key, params object?[] args)
        => Get(key, args);

    public static Dictionary<string, string> Load(string languageCode)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "localization", $"strings.{languageCode}.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Empty dictionary: {path}");
    }
}
