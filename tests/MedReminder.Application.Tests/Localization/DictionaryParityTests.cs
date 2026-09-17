using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Localization;

// Every localized dictionary (it, fr, es, de) must define exactly
// the same set of keys as the reference English dictionary — no
// missing keys, no extras. Prevents drift as new UI strings are
// added.
public class DictionaryParityTests
{
    [Theory]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Localized_dictionary_covers_every_english_key(string languageCode)
    {
        var reference = LoadDictionary(SupportedLanguages.Default);
        var localized = LoadDictionary(languageCode);

        var missing = reference.Keys.Except(localized.Keys).OrderBy(k => k).ToArray();
        var extra = localized.Keys.Except(reference.Keys).OrderBy(k => k).ToArray();

        missing.Should().BeEmpty($"'{languageCode}' is missing translations for these English keys");
        extra.Should().BeEmpty($"'{languageCode}' defines keys absent from the English reference");
    }

    private static Dictionary<string, string> LoadDictionary(string languageCode)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "localization",
            $"strings.{languageCode}.json");
        File.Exists(path).Should().BeTrue($"expected dictionary at {path}");
        using var stream = File.OpenRead(path);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        dict.Should().NotBeNull();
        return dict!;
    }
}
