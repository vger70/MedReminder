using System.Globalization;
using FluentAssertions;
using MedReminder.Application.Export;
using Xunit;

namespace MedReminder.Application.Tests.Export;

// The C.3+ snapshot name (§3.3) is written by the backup host, pruned by
// retention and parsed by the restore list: one definition, so a name
// is either a snapshot for all three or for none.
public class CloudSnapshotNameTests
{
    private static readonly string Profile = new('a', 32);
    private static readonly DateTimeOffset At = new(2026, 9, 21, 8, 30, 15, TimeSpan.Zero);

    [Fact]
    public void Created_name_round_trips()
    {
        var name = CloudSnapshotName.Create(Profile, At);

        name.Should().Be($"medreminder-{Profile}-20260921-083015.mrz");
        CloudSnapshotName.IsMatch(name).Should().BeTrue();
        CloudSnapshotName.TryParse(name, out var profileId, out var created).Should().BeTrue();
        profileId.Should().Be(Profile);
        created.Should().Be(At);
    }

    [Fact]
    public void The_stamp_is_utc()
    {
        var local = new DateTimeOffset(2026, 9, 21, 10, 30, 15, TimeSpan.FromHours(2));

        CloudSnapshotName.Create(Profile, local).Should().Be($"medreminder-{Profile}-20260921-083015.mrz");
    }

    [Fact]
    public void The_stamp_is_gregorian_whatever_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            var name = CloudSnapshotName.Create(Profile, At);

            name.Should().Contain("-20260921-");
            CloudSnapshotName.TryParse(name, out _, out var created).Should().BeTrue();
            created.Should().Be(At);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("medreminder-default-20260921-083015.mrz", true)]
    [InlineData("medreminder-abc-def-20260921-083015.mrz", false)]
    [InlineData("medreminder-export-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-20260921-083015.mrz", false)]
    [InlineData("medreminder-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-20260921-083015.MRZ", false)]
    [InlineData("medreminder-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-20260921-083015.db", false)]
    [InlineData("copied-by-hand.mrz", false)]
    public void Only_the_exact_pattern_matches(string name, bool expected)
    {
        CloudSnapshotName.IsMatch(name).Should().Be(expected);
        CloudSnapshotName.TryParse(name, out var profileId, out _).Should().Be(expected);
        if (!expected) profileId.Should().BeEmpty();
    }
}
