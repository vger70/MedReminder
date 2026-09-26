using FluentAssertions;
using MedReminder.Application.Catalogue;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

public sealed class ScanBurstDetectorTests
{
    [Fact]
    public void Fast_keystrokes_are_a_burst()
    {
        var detector = Record(count: 10, intervalMs: 8);

        detector.IsBurst(minimumKeystrokes: 6, maxAverageIntervalMilliseconds: 50).Should().BeTrue();
    }

    [Fact]
    public void Human_typing_is_not_a_burst()
    {
        var detector = Record(count: 10, intervalMs: 180);

        detector.IsBurst(minimumKeystrokes: 6, maxAverageIntervalMilliseconds: 50).Should().BeFalse();
    }

    [Fact]
    public void Too_few_keystrokes_are_not_a_burst()
    {
        var detector = Record(count: 5, intervalMs: 5);

        detector.IsBurst(minimumKeystrokes: 6, maxAverageIntervalMilliseconds: 50).Should().BeFalse();
    }

    [Fact]
    public void Average_interval_at_the_bound_is_a_burst()
    {
        var detector = Record(count: 6, intervalMs: 50);

        detector.IsBurst(minimumKeystrokes: 6, maxAverageIntervalMilliseconds: 50).Should().BeTrue();
    }

    [Fact]
    public void Reset_forgets_previous_keystrokes()
    {
        var detector = Record(count: 10, intervalMs: 5);

        detector.Reset();

        detector.KeystrokeCount.Should().Be(0);
        detector.IsBurst(minimumKeystrokes: 6, maxAverageIntervalMilliseconds: 50).Should().BeFalse();
    }

    private static ScanBurstDetector Record(int count, long intervalMs)
    {
        var detector = new ScanBurstDetector();
        for (var i = 0; i < count; i++)
        {
            detector.Record(1_000 + i * intervalMs);
        }
        return detector;
    }
}
