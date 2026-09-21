using FluentAssertions;
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
}
