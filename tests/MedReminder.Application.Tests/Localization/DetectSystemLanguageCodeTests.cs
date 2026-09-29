using System.Globalization;
using FluentAssertions;
using MedReminder.Application.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.Localization;

public class DetectSystemLanguageCodeTests
{
    [Theory]
    [InlineData("de", "de")]
    [InlineData("de-DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("de-CH", "de")]
    [InlineData("it", "it")]
    [InlineData("it-IT", "it")]
    [InlineData("fr", "fr")]
    [InlineData("fr-CA", "fr")]
    [InlineData("es", "es")]
    [InlineData("es-MX", "es")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("zh-CN", "en")]
    [InlineData("pt-BR", "en")]
    public void Maps_current_ui_culture_to_supported_language_code(string culture, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            NotificationTexts.DetectSystemLanguageCode().Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
