using FluentAssertions;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Domain.Tests.Medicines;

// Time-of-day presets and per-day default times
// (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md §4.2, §6).
public class DoseTimePresetTests
{
    [Fact]
    public void Built_in_ids_are_stable_and_unique()
    {
        BuiltInDoseTimePresets.All.Select(d => d.Id).Should().OnlyHaveUniqueItems();
        BuiltInDoseTimePresets.IdOf("Morning").Should().Be(BuiltInDoseTimePresets.IdOf("Morning"));
        BuiltInDoseTimePresets.Find(BuiltInDoseTimePresets.IdOf("BeforeLunch"))!.Time
            .Should().Be(new TimeOnly(13, 0));
    }

    [Fact]
    public void Only_the_as_needed_built_in_has_no_time()
    {
        BuiltInDoseTimePresets.All.Where(d => d.Time is null).Should().ContainSingle()
            .Which.Should().Match<BuiltInDoseTimePresets.Definition>(d =>
                d.Key == BuiltInDoseTimePresets.AsNeededKey && d.IsAsNeeded);
    }

    [Fact]
    public void Default_times_cover_one_to_four_administrations()
    {
        DoseTimeDefault.BuiltIn.Keys.Should().Equal(1, 2, 3, 4);
        foreach (var (count, times) in DoseTimeDefault.BuiltIn) times.Should().HaveCount(count);
        DoseTimeDefault.BuiltIn[3].Should().Equal(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(20, 0));
    }

    [Theory]
    [InlineData("08:00; 20:00", 2, true)]
    [InlineData("20:00,8:00", 2, true)]
    [InlineData("08:00", 2, false)]
    [InlineData("08:00;25:00", 2, false)]
    [InlineData("", 1, false)]
    public void Parse_accepts_exactly_the_count_of_valid_times(string text, int count, bool valid)
    {
        var parsed = DoseTimeDefault.Parse(text, count);

        (parsed is not null).Should().Be(valid);
        if (valid) parsed.Should().BeInAscendingOrder().And.HaveCount(count);
    }

    [Fact]
    public void Format_and_parse_round_trip()
    {
        var times = DoseTimeDefault.BuiltIn[4];

        DoseTimeDefault.Parse(DoseTimeDefault.Format(times), 4).Should().Equal(times);
        DoseTimeDefault.Format(times).Should().Be("08:00;12:00;16:00;20:00");
    }
}
