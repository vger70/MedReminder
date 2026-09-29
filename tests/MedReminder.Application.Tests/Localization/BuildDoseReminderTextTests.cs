using System.Globalization;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Application.Tests.Localization;

// A5 dose-time reminder text. Without a localization service the
// builder falls back to hardcoded English, mirroring BuildToast.
public class BuildDoseReminderTextTests
{
    private static Medicine NewMedicine(string name)
        => new() { Name = name, Unit = "compresse" };

    [Fact]
    public void Fallback_english_uses_name_and_24h_time()
    {
        var (title, body) = NotificationTexts.BuildDoseReminder(
            NewMedicine("Enalapril"), new TimeOnly(9, 0));

        title.Should().Be("Time to take Enalapril");
        body.Should().Be("Enalapril — scheduled dose at 09:00");
    }

    [Fact]
    public void Fallback_english_formats_afternoon_time_in_24h()
    {
        var (_, body) = NotificationTexts.BuildDoseReminder(
            NewMedicine("Metformina"), new TimeOnly(20, 30));

        body.Should().Be("Metformina — scheduled dose at 20:30");
    }

    [Fact]
    public void Dose_reminder_uses_the_user_language_not_a_forced_one()
    {
        var (title, body) = NotificationTexts.BuildDoseReminder(
            NewMedicine("Enalapril"), new TimeOnly(9, 0), new UserLanguageOnlyLocalization());

        title.Should().Be("user:Notifications.DoseReminder.Title");
        body.Should().Be("user:Notifications.DoseReminder.Body");
    }

    [Fact]
    public void Low_stock_toast_uses_the_user_language_not_a_forced_one()
    {
        var (title, body) = NotificationTexts.BuildToast(
            NewMedicine("Enalapril"), 5, localization: new UserLanguageOnlyLocalization());

        title.Should().Be("user:Notifications.Toast.Title");
        body.Should().Be("user:Notifications.Toast.Body");
    }

    // Distinguishes Get (user language) from GetIn (forced language),
    // so the test fails if a builder bypasses the user's choice.
    private sealed class UserLanguageOnlyLocalization : ILocalizationService
    {
        public string CurrentLanguage => "en";

        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

        public string Get(string key, params object?[] args) => $"user:{key}";

        public string GetIn(string languageCode, string key, params object?[] args)
            => $"forced-{languageCode}:{key}";
    }
}
