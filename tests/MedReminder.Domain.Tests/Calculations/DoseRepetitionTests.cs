using FluentAssertions;
using MedReminder.Domain.Calculations;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

// Mobile issue #128: the instants of the repetitions of a dose reminder.
public class DoseRepetitionTests
{
    private static readonly DateTimeOffset Slot = new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Repeat_starts_one_interval_after_the_slot_and_delayed_insistence_after_its_delay()
    {
        var repeat = DoseRepetition.Repeat(TimeSpan.FromMinutes(10), 6);
        var delayed = new DoseRepetition(TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(10), 6);

        repeat.At(Slot, 0).Should().Be(Slot);
        repeat.At(Slot, 1).Should().Be(Slot.AddMinutes(10));
        repeat.At(Slot, 6).Should().Be(Slot.AddMinutes(60));
        delayed.At(Slot, 1).Should().Be(Slot.AddMinutes(60));
        delayed.At(Slot, 6).Should().Be(Slot.AddMinutes(110));
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 2)]
    [InlineData(500, 6)]
    public void The_last_repetition_due(int minutesAfterSlot, int expected)
        => DoseRepetition.Repeat(TimeSpan.FromMinutes(10), 6).LatestDue(Slot, Slot.AddMinutes(minutesAfterSlot))
            .Should().Be(expected);

    [Fact]
    public void Keys_tell_the_repetitions_apart_and_keep_the_slot_key_for_its_own_reminder()
    {
        var id = Guid.NewGuid();

        DoseRepetition.SlotKey(id, 0).Should().Be(id.ToString());
        DoseRepetition.SlotKey(id, DoseRepetition.MaxCount).Should().Be($"{id}#99").And.HaveLength(39);
    }

    [Fact]
    public void Invalid_values_are_refused()
    {
        var act = () => new DoseRepetition(TimeSpan.Zero, TimeSpan.FromMinutes(10), 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => DoseRepetition.Repeat(TimeSpan.FromMinutes(10), 0))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => DoseRepetition.Repeat(TimeSpan.FromMinutes(10), 100))).Should().Throw<ArgumentOutOfRangeException>();
    }
}
