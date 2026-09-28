using System.Globalization;
using FluentAssertions;
using MedReminder.Application.Reporting;
using MedReminder.Application.Tests.Support;
using Xunit;
using static MedReminder.Application.Tests.Reporting.TherapyReportTestData;

namespace MedReminder.Application.Tests.Reporting;

public class TherapyCardBuilderTests
{
    private static TherapyCard Build(
        IReadOnlyList<TherapyReportEntry> entries,
        TherapyCardOptions? options = null,
        string? profileName = null,
        string language = "en")
        => TherapyCardBuilder.Build(entries, ReportDate, options, profileName,
            localization: new DictionaryLocalizationService(language));

    [Fact]
    public void No_active_medicines_yields_no_rows_and_the_empty_message()
    {
        var card = Build([InactiveAspirin()]);

        card.Rows.Should().BeEmpty();
        card.EmptyMessage.Should().Be("No active medicine.");
        card.Disclaimer.Should().Contain("not a medical device");
    }

    [Fact]
    public void Many_medicines_are_all_kept_in_name_order_with_sequential_indexes()
    {
        var entries = Enumerable.Range(1, 60).Reverse().Select(Numbered).ToList();

        var card = Build(entries);

        card.Rows.Should().HaveCount(60);
        card.Rows.Select(r => r.Index).Should().Equal(Enumerable.Range(1, 60));
        card.Rows.Select(r => r.Name).Should().BeInAscendingOrder(StringComparer.CurrentCultureIgnoreCase);
        card.Rows[0].Name.Should().Be("Medicine 001");
    }

    [Fact]
    public void Missing_optional_fields_stay_empty()
    {
        var row = Build([Metformin()], new TherapyCardOptions(IncludeNotes: true)).Rows.Single();

        row.ActiveIngredient.Should().BeNull();
        row.StartDate.Should().BeNull();
        row.EndDate.Should().BeNull();
        row.PeriodLines.Should().BeEmpty();
        row.Doctor.Should().BeNull();
        row.Notes.Should().BeNull();
        row.DosageLines.Should().Equal("1 tablets × 2 times a day");
    }

    [Fact]
    public void Full_row_carries_slots_period_and_doctor()
    {
        var row = Build([Enalapril()]).Rows.Single();

        row.Name.Should().Be("Enalapril");
        row.ActiveIngredient.Should().Be("enalapril maleate");
        row.DosageLines.Should().Equal("1 tablets breakfast (08:00)", "0.5 tablets (20:00)");
        row.StartDate.Should().Be("09/01/2026");
        row.EndDate.Should().Be("12/31/2026");
        row.PeriodLines.Should().Equal("from 09/01/2026", "until 12/31/2026");
        row.Doctor.Should().Be("Dr. Example");
    }

    [Fact]
    public void Notes_are_left_out_by_default()
    {
        var card = Build([Enalapril()]);

        card.IncludesNotes.Should().BeFalse();
        card.Rows.Single().Notes.Should().BeNull();
    }

    [Fact]
    public void Notes_are_included_when_the_option_is_on()
    {
        var card = Build([Enalapril()], new TherapyCardOptions(IncludeNotes: true));

        card.IncludesNotes.Should().BeTrue();
        card.Rows.Single().Notes.Should().Be("after meals");
        card.Columns.Notes.Should().Be("Notes");
    }

    [Fact]
    public void Profile_line_is_present_only_when_a_name_is_given()
    {
        Build([Enalapril()], profileName: "Owner").ProfileLine.Should().Be("Profile: Owner");
        Build([Enalapril()]).ProfileLine.Should().BeNull();
        Build([Enalapril()], profileName: "  ").ProfileLine.Should().BeNull();
    }

    [Fact]
    public void Without_localization_english_fallbacks_are_used()
    {
        var card = TherapyCardBuilder.Build(
            [Enalapril()], ReportDate, culture: CultureInfo.InvariantCulture);

        card.Title.Should().Be("Therapy card — MedReminder");
        card.DateLine.Should().Be("Date: 09/28/2026");
        card.Columns.Should().Be(new TherapyCardColumns("Medicine", "Dosage", "Period", "Doctor", "Notes"));
        card.Rows.Single().PeriodLines.Should().Equal("from 09/01/2026", "until 12/31/2026");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Every_supported_language_has_all_card_strings(string language)
    {
        var dict = DictionaryLocalizationService.Load(language);

        var card = Build([Enalapril(), Metformin()], new TherapyCardOptions(IncludeNotes: true), "Owner", language);

        card.Title.Should().Be(dict["Reports.Therapy.Header"]);
        card.Columns.Should().Be(new TherapyCardColumns(
            dict["Reports.Therapy.Column.Medicine"],
            dict["Reports.Therapy.Column.Dosage"],
            dict["Reports.Therapy.Column.Period"],
            dict["Reports.Therapy.Column.Doctor"],
            dict["Reports.Therapy.Column.Notes"]));
        card.Disclaimer.Should().Be(dict["Reports.Therapy.Disclaimer"]);

        var strings = new[] { card.Title, card.DateLine, card.ProfileLine!, card.EmptyMessage, card.Disclaimer }
            .Concat(card.Rows.SelectMany(r => r.DosageLines.Concat(r.PeriodLines)));
        // The localization service returns "[key]" for a missing key.
        strings.Should().NotContain(s => s.StartsWith("[Reports.", StringComparison.Ordinal));
        dict.Should().ContainKey("Reports.Therapy.Page");
    }
}
