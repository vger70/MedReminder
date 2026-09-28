using System.Globalization;
using FluentAssertions;
using MedReminder.Application.Reporting;
using MedReminder.Application.Tests.Support;
using Xunit;
using static MedReminder.Application.Tests.Reporting.TherapyReportTestData;

namespace MedReminder.Application.Tests.Reporting;

// Plain-text renderer. Pins the layout the card had before the
// structured model was introduced (Build keeps notes, as it always
// did).
public class TherapyReportTests
{
    private static readonly string NL = Environment.NewLine;

    [Fact]
    public void Build_without_localization_keeps_the_text_layout()
    {
        var text = TherapyReport.Build(
            [Metformin(), InactiveAspirin(), Enalapril()], ReportDate, CultureInfo.InvariantCulture);

        text.Should().Be(string.Join(NL,
            "Therapy card — MedReminder",
            "Date: 09/28/2026",
            "",
            "1. Enalapril (enalapril maleate)",
            "   - 1 tablets breakfast (08:00)",
            "   - 0.5 tablets (20:00)",
            "   Start: 09/01/2026 · End: 12/31/2026",
            "   Doctor: Dr. Example",
            "   Notes: after meals",
            "",
            "2. Metformin",
            "   - 1 tablets × 2 times a day",
            "",
            "— MedReminder: organizational reminder, not a medical device."));
    }

    [Fact]
    public void Build_with_localization_uses_the_dictionary_labels()
    {
        var loc = new DictionaryLocalizationService("en");

        var text = TherapyReport.Build([Enalapril(), Metformin()], ReportDate, localization: loc);

        text.Should().Be(string.Join(NL,
            "Therapy sheet — MedReminder",
            "Date: 09/28/2026",
            "",
            "1. Enalapril (enalapril maleate)",
            "   - 1 tablets breakfast (08:00)",
            "   - 0.5 tablets (20:00)",
            "   Start: 09/01/2026 · End: 12/31/2026",
            "   Doctor: Dr. Example",
            "   Notes: after meals",
            "",
            "2. Metformin",
            "   - 1 tablets × 2 times a day",
            "",
            "— MedReminder: organizational reminder, not a medical device."));
    }

    [Fact]
    public void Build_with_no_active_medicines_prints_only_the_message()
    {
        var text = TherapyReport.Build([InactiveAspirin()], ReportDate, CultureInfo.InvariantCulture);

        text.Should().Be(string.Join(NL,
            "Therapy card — MedReminder",
            "Date: 09/28/2026",
            "",
            "No active medicines.",
            ""));
    }

    [Fact]
    public void RenderText_adds_the_profile_line_and_omits_notes_when_off()
    {
        var loc = new DictionaryLocalizationService("en");
        var card = TherapyCardBuilder.Build(
            [Enalapril()], ReportDate, new TherapyCardOptions(), "Owner", localization: loc);

        var text = TherapyReport.RenderText(card, loc);

        text.Should().StartWith(string.Join(NL,
            "Therapy sheet — MedReminder",
            "Profile: Owner",
            "Date: 09/28/2026",
            ""));
        text.Should().NotContain("after meals");
    }
}
