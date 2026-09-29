using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.Timeline;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Application.Tests.Timeline;

public partial class TherapyTimelineTextTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);
    private static readonly TimelineWindow Window = TimelineWindow.Around(Today);
    private static readonly TherapyTimelineText Text = new(new DictionaryLocalizationService("en"));

    private static TherapyTimelineRow BuildRow(
        Medicine m,
        IReadOnlyList<MedicationSuspension>? suspensions = null,
        decimal stock = 30m)
        => TherapyTimelineBuilder.BuildRow(
            new TherapyTimelineInput(
                m,
                [new MedicationScheduleHistory
                {
                    MedicineId = m.Id,
                    EffectiveFrom = m.StartDate,
                    DosePerAdministration = 1m,
                    AdministrationsPerDay = 2,
                }],
                suspensions ?? [],
                [],
                stock),
            Today, Window);

    private static Medicine NewMedicine(bool isActive = true, DateOnly? end = null)
        => new()
        {
            Name = "Enalapril",
            Unit = "tablets",
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = end,
            IsActive = isActive,
        };

    [Fact]
    public void Describes_each_schedule_kind()
    {
        Text.DescribeSchedule(new FixedDailySchedule(1m, 2), "tablets")
            .Should().Be("1 tablets × 2 a day");
        Text.DescribeSchedule(new PrnSchedule(), "tablets").Should().Be("as needed");
        Text.DescribeSchedule(new CyclicSchedule(21, 7, 1m), "tablets")
            .Should().Be("1 tablets a day, 21 days on and 7 days off");
        Text.DescribeSchedule(new WeeklySchedule([1m, 0m, 1m, 0m, 1m, 0m, 0m]), "tablets")
            .Should().Be("weekly pattern, 3 tablets per week");
        Text.DescribeSchedule(new TaperingSchedule(4m, 1m, 1m, 7), "tablets")
            .Should().Be("taper from 4 to 1 tablets a day, step 1 every 7 days");
        Text.DescribeSchedule(
                new SteppedTaperingSchedule([new TaperStage(4m, 7), new TaperStage(2m, 7)], maintainLastDose: true),
                "tablets")
            .Should().Be("stepped taper: 4 tablets a day for 7 days → 2 tablets a day for 7 days, last dose maintained");
    }

    [Fact]
    public void Run_out_is_worded_as_an_estimate()
    {
        var row = BuildRow(NewMedicine());

        Text.DescribeRunOut(row).Should().Be(
            "Estimated run-out: 09/28/2026 (15 days left at today's daily quantity)");
    }

    [Fact]
    public void Run_out_after_planned_end_is_mentioned()
    {
        var row = BuildRow(NewMedicine(end: new DateOnly(2026, 9, 20)));

        Text.DescribeRunOut(row).Should().EndWith("This is after the planned end of therapy.");
    }

    [Fact]
    public void Details_list_suspensions_with_reason_and_dosage_changes()
    {
        var m = NewMedicine();
        var suspension = new MedicationSuspension
        {
            MedicineId = m.Id,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 10),
            Reason = "surgery",
        };

        var details = Text.DescribeRow(BuildRow(m, [suspension]));

        details.Should().Contain("Therapy: from 09/01/2026, no end date");
        details.Should().Contain("Current dosage: 1 tablets × 2 a day");
        details.Should().Contain("Suspended from 10/01/2026 to 10/10/2026 — reason: surgery");
        details.Should().Contain("09/01/2026: therapy starts with 1 tablets × 2 a day");
        details.Should().Contain("Estimated run-out: 09/28/2026");
    }

    [Fact]
    public void Suspended_today_explains_the_missing_forecast()
    {
        var m = NewMedicine();
        var open = new MedicationSuspension { MedicineId = m.Id, StartDate = new DateOnly(2026, 9, 10) };
        var row = BuildRow(m, [open]);

        Text.DescribeRunOut(row).Should().Be(
            "Estimated run-out: not available while the therapy is suspended.");
        Text.DescribeSegment(row, row.Segments[^1]).Should().Be("Suspended from 09/10/2026, no end date");
    }

    [Fact]
    public void Inactive_summary_is_tagged_and_omits_the_run_out()
    {
        var summary = Text.Summarize(BuildRow(NewMedicine(isActive: false)));

        summary.Should().Contain("(deactivated)");
        summary.Should().NotContain("run-out");
    }

    [Fact]
    public void Active_segment_wording_reflects_window_edges()
    {
        var row = BuildRow(new Medicine
        {
            Name = "Enalapril",
            Unit = "tablets",
            StartDate = new DateOnly(2026, 1, 1),
        });

        Text.DescribeSegment(row, row.Segments[0]).Should().Be("Active for the whole period shown");
    }

    [Theory]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Translations_use_the_same_placeholders_as_english(string languageCode)
    {
        var english = DictionaryLocalizationService.Load("en");
        var localized = DictionaryLocalizationService.Load(languageCode);
        var args = new object?[] { "a", "b", "c", "d", "e", "f" };

        foreach (var (key, value) in english.Where(kv => kv.Key.StartsWith("Ui.TherapyTimeline.", StringComparison.Ordinal)))
        {
            localized.Should().ContainKey(key);
            Placeholders(localized[key]).Should().BeEquivalentTo(Placeholders(value), key);
            var format = () => string.Format(CultureInfo.InvariantCulture, localized[key], args);
            format.Should().NotThrow(key);
        }
    }

    private static IEnumerable<string> Placeholders(string value)
        => PlaceholderRegex().Matches(value).Select(m => m.Value).Distinct();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderRegex();
}
