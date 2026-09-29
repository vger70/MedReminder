using FluentAssertions;
using MedReminder.Application.Catalogue;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

public class SnapshotVersionTests
{
    [Theory]
    [InlineData("202609", null, true)]
    [InlineData("202610", "202609", true)]
    [InlineData("202701", "202612", true)]
    [InlineData("202609", "202609", false)]
    [InlineData("202608", "202609", false)]
    [InlineData("v2", "v1", true)]
    [InlineData("v1", "v2", true)]
    [InlineData("v1", "v1", false)]
    [InlineData("202609", "v1", true)]
    // Month + build-time suffix (remote imports).
    [InlineData("202610+20261005T030012Z", "202610+20261002T030000Z", true)]
    [InlineData("202610+20261002T030000Z", "202610+20261005T030012Z", false)]
    [InlineData("202610+20261002T030000Z", "202610+20261002T030000Z", false)]
    [InlineData("202610+20261002T030000Z", "202610", true)]
    [InlineData("202610", "202610+20261002T030000Z", false)]
    [InlineData("202609", "202610+20261002T030000Z", false)]
    [InlineData("202611", "202610+20261031T235959Z", true)]
    [InlineData("202610+20261002T030000Z", "202609", true)]
    // A malformed suffix is not the known format: any difference counts.
    [InlineData("202610+garbage", "202610", true)]
    public void IsNewer_follows_yyyymm_order_and_falls_back_to_difference(
        string candidate, string? current, bool expected)
    {
        SnapshotVersion.IsNewer(candidate, current).Should().Be(expected);
    }

    [Theory]
    [InlineData("202609", true)]
    [InlineData("202601", true)]
    [InlineData("202612", true)]
    [InlineData("202600", false)]
    [InlineData("202613", false)]
    [InlineData("20260", false)]
    [InlineData("2026091", false)]
    [InlineData("2026a9", false)]
    [InlineData(null, false)]
    public void IsYearMonth_accepts_six_digits_with_a_valid_month(string? version, bool expected)
    {
        SnapshotVersion.IsYearMonth(version).Should().Be(expected);
    }

    [Fact]
    public void Compose_appends_the_UTC_build_time_to_the_month()
    {
        var generated = new DateTimeOffset(2026, 10, 5, 5, 0, 12, TimeSpan.FromHours(2)).AddTicks(1234);

        SnapshotVersion.Compose("202610", generated).Should().Be("202610+20261005T030012Z");
        SnapshotVersion.Compose("202610", null).Should().Be("202610");
    }

    [Fact]
    public void Compose_rejects_a_label_that_is_not_yyyymm()
    {
        var act = () => SnapshotVersion.Compose("2026-10", DateTimeOffset.UnixEpoch);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("202610", "202610", null)]
    [InlineData("202610+20261005T030012Z", "202610", "20261005T030012Z")]
    public void TryParse_splits_month_and_suffix(string label, string month, string? suffix)
    {
        SnapshotVersion.TryParse(label, out var parsedMonth, out var parsedSuffix).Should().BeTrue();

        parsedMonth.Should().Be(month);
        parsedSuffix.Should().Be(suffix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("202613")]
    [InlineData("202610+")]
    [InlineData("202610+20261005T030012")]
    [InlineData("202610+20261305T030012Z")]
    [InlineData("202610+2026-10-05T03:00Z")]
    public void TryParse_rejects_other_formats(string? label)
    {
        SnapshotVersion.TryParse(label, out _, out _).Should().BeFalse();
    }
}
