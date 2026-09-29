using FluentAssertions;
using MedReminder.Application.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Localization;

public class SupportedLanguagesTests
{
    [Fact]
    public void All_contains_the_five_shipped_languages()
    {
        var codes = SupportedLanguages.All.Select(l => l.Code).ToArray();
        codes.Should().BeEquivalentTo(new[] { "en", "it", "fr", "es", "de" });
    }

    [Theory]
    [InlineData("de", "de", "Deutsch")]
    [InlineData("DE", "de", "Deutsch")]
    [InlineData("en", "en", "English")]
    [InlineData("it", "it", "Italiano")]
    [InlineData("fr", "fr", "Français")]
    [InlineData("es", "es", "Español")]
    public void Resolve_returns_the_matching_supported_language(string input, string expectedCode, string expectedDisplayName)
    {
        var lang = SupportedLanguages.Resolve(input);

        lang.Code.Should().Be(expectedCode);
        lang.DisplayName.Should().Be(expectedDisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zh-CN")]
    [InlineData("pt")]
    public void Resolve_falls_back_to_default_for_unsupported_input(string? input)
    {
        var lang = SupportedLanguages.Resolve(input);

        lang.Code.Should().Be(SupportedLanguages.Default);
    }
}
