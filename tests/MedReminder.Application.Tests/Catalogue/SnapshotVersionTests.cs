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
}
