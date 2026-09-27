using FluentAssertions;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Domain.Tests.Sync;

public class RegisterMergeTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static HybridTimestamp T(long ms, Guid device) => new(ms, 0, device);

    private static RegisterMerge.Version V(long ms, Guid device, string value) => new(T(ms, device), null, value);

    [Fact]
    public void Winner_is_the_greatest_timestamp()
        => RegisterMerge.Winner([V(1, A, "a"), V(3, B, "b"), V(2, A, "c")])!.Value.Should().Be("b");

    [Fact]
    public void A_write_over_the_other_version_is_not_concurrent()
    {
        var first = new RegisterMerge.Version(T(1, A), null, "a");
        var second = new RegisterMerge.Version(T(2, B), T(1, A), "b");

        RegisterMerge.AreConcurrent(first, second).Should().BeFalse();
        RegisterMerge.Losers([first, second]).Should().BeEmpty();
    }

    [Fact]
    public void Writes_that_did_not_see_each_other_are_concurrent_and_the_older_loses()
    {
        var genesis = new RegisterMerge.Version(T(1, A), null, "g");
        var a = new RegisterMerge.Version(T(5, A), T(1, A), "a");
        var b = new RegisterMerge.Version(T(4, B), T(1, A), "b");

        RegisterMerge.AreConcurrent(a, b).Should().BeTrue();
        RegisterMerge.Losers([genesis, a, b]).Should().Equal(b);
    }

    [Fact]
    public void A_later_write_that_saw_both_clears_the_conflict()
    {
        var a = new RegisterMerge.Version(T(5, A), null, "a");
        var b = new RegisterMerge.Version(T(4, B), null, "b");
        var c = new RegisterMerge.Version(T(9, B), T(5, A), "c");

        RegisterMerge.Losers([a, b, c]).Should().BeEmpty();
    }

    [Fact]
    public void Result_does_not_depend_on_the_order_of_the_versions()
    {
        var versions = new[]
        {
            new RegisterMerge.Version(T(5, A), null, "a"),
            new RegisterMerge.Version(T(4, B), null, "b"),
            new RegisterMerge.Version(T(6, B), T(4, B), "c"),
        };

        var expected = RegisterMerge.Losers(versions);
        foreach (var order in new[] { versions.Reverse().ToArray(), [versions[1], versions[2], versions[0]] })
        {
            RegisterMerge.Losers(order).Should().Equal(expected);
            RegisterMerge.Winner(order).Should().Be(RegisterMerge.Winner(versions));
        }
        expected.Should().Equal(versions[0]);
    }
}
