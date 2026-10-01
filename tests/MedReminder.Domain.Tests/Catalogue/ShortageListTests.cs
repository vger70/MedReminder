using FluentAssertions;
using MedReminder.Domain.Catalogue;
using Xunit;

namespace MedReminder.Domain.Tests.Catalogue;

// Shortage list (docs/notes/EVOLUTION-PROPOSALS-2.md §3.3).
public class ShortageListTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static ShortageList List() => new(CountryCode.Parse("IT"), new DateOnly(2026, 9, 29),
    [
        new ShortageEntry("045348036", new DateOnly(2025, 7, 28), null, true, ShortageReason.Production),
        new ShortageEntry("012345678", new DateOnly(2028, 5, 1), null, false, ShortageReason.Withdrawn),
        new ShortageEntry("037839115", new DateOnly(2026, 8, 10), new DateOnly(2026, 9, 1), true, ShortageReason.Demand),
    ]);

    [Fact]
    public void A_started_shortage_is_current_and_an_announced_one_expected()
    {
        List().NoticeFor("045348036", Today)!.State.Should().Be(ShortageState.Current);
        List().NoticeFor("012345678", Today)!.State.Should().Be(ShortageState.Expected);
        List().NoticeFor("012345678", new DateOnly(2028, 5, 1))!.State.Should().Be(ShortageState.Current);
    }

    [Fact]
    public void A_shortage_past_its_expected_end_stays_current_while_listed()
    {
        List().NoticeFor("037839115", Today)!.State.Should().Be(ShortageState.Current);
    }

    [Fact]
    public void Codes_not_listed_or_missing_have_no_notice()
    {
        List().NoticeFor("999999999", Today).Should().BeNull();
        List().NoticeFor(null, Today).Should().BeNull();
        List().NoticeFor(" 045348036 ", Today).Should().NotBeNull("codes are trimmed");
        List().Count.Should().Be(3);
    }
}
