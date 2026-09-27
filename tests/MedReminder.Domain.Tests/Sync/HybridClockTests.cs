using FluentAssertions;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Domain.Tests.Sync;

public class HybridClockTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    [Fact]
    public void First_tick_uses_the_wall_clock()
        => HybridClock.Tick(null, 1_000, A).Should().Be(new HybridTimestamp(1_000, 0, A));

    [Fact]
    public void Tick_follows_a_clock_that_moves_forward()
        => HybridClock.Tick(new HybridTimestamp(1_000, 7, A), 2_000, A)
            .Should().Be(new HybridTimestamp(2_000, 0, A));

    [Theory]
    [InlineData(1_000)] // same millisecond
    [InlineData(500)]   // clock went backwards
    public void Tick_never_goes_back(long now)
    {
        var last = new HybridTimestamp(1_000, 3, A);

        var next = HybridClock.Tick(last, now, A);

        next.Should().Be(new HybridTimestamp(1_000, 4, A));
        next.Should().BeGreaterThan(last);
    }

    [Fact]
    public void Receive_from_a_device_ahead_stays_after_the_remote_timestamp()
    {
        var remote = new HybridTimestamp(5_000, 2, B);

        var next = HybridClock.Receive(new HybridTimestamp(1_000, 0, A), remote, 1_500, A);

        next.Should().Be(new HybridTimestamp(5_000, 3, A));
        next.Should().BeGreaterThan(remote);
    }

    [Fact]
    public void Receive_when_all_three_share_the_millisecond_takes_the_larger_counter()
        => HybridClock.Receive(new HybridTimestamp(1_000, 4, A), new HybridTimestamp(1_000, 9, B), 1_000, A)
            .Should().Be(new HybridTimestamp(1_000, 10, A));

    [Fact]
    public void Receive_of_an_old_timestamp_follows_the_local_state()
        => HybridClock.Receive(new HybridTimestamp(3_000, 1, A), new HybridTimestamp(1_000, 0, B), 2_000, A)
            .Should().Be(new HybridTimestamp(3_000, 2, A));

    [Fact]
    public void Receive_with_the_wall_clock_ahead_resets_the_counter()
        => HybridClock.Receive(new HybridTimestamp(1_000, 4, A), new HybridTimestamp(1_500, 9, B), 9_000, A)
            .Should().Be(new HybridTimestamp(9_000, 0, A));

    [Fact]
    public void Order_is_physical_then_counter_then_device()
    {
        var items = new[]
        {
            new HybridTimestamp(2, 0, A),
            new HybridTimestamp(1, 1, A),
            new HybridTimestamp(1, 0, B),
            new HybridTimestamp(1, 0, A),
        };

        items.Order().Should().Equal(
            new HybridTimestamp(1, 0, A),
            new HybridTimestamp(1, 0, B),
            new HybridTimestamp(1, 1, A),
            new HybridTimestamp(2, 0, A));
    }

    [Fact]
    public void Device_tie_break_compares_the_hex_text_not_Guid_CompareTo()
    {
        // The rule is the text order, which any store or platform can
        // reproduce; these pairs pin it at the sign bit and in the last byte.
        var low = Guid.Parse("7fffffff-0000-0000-0000-000000000000");
        var high = Guid.Parse("80000000-0000-0000-0000-000000000000");

        new HybridTimestamp(1, 0, low).Should().BeLessThan(new HybridTimestamp(1, 0, high));
        new HybridTimestamp(1, 0, Guid.Parse("00000000-0000-0000-0000-0000000000ff"))
            .Should().BeGreaterThan(new HybridTimestamp(1, 0, Guid.Parse("00000000-0000-0000-0000-00000000000f")));
    }
}
