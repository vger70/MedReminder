using FluentAssertions;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Domain.Tests.Stock;

public class PackageAllocationTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static StockPackage Package(
        decimal quantity, DateOnly? expiresOn = null, DateOnly? openedOn = null, int recordedDay = 0) => new()
    {
        MedicineId = Guid.Empty,
        Quantity = quantity,
        ExpiresOn = expiresOn,
        OpenedOn = openedOn,
        RecordedAt = At.AddDays(recordedDay),
    };

    [Fact]
    public void Opened_packages_come_first_then_the_first_expiring_then_those_without_expiry()
    {
        var noExpiry = Package(28m);
        var late = Package(28m, new DateOnly(2027, 12, 31));
        var early = Package(28m, new DateOnly(2027, 3, 31));
        var opened = Package(28m, new DateOnly(2028, 1, 31), openedOn: new DateOnly(2026, 9, 20));

        PackageAllocation.ConsumptionOrder([noExpiry, late, early, opened])
            .Should().Equal(opened, early, late, noExpiry);
    }

    [Fact]
    public void The_stock_sits_in_the_packages_used_last()
    {
        var early = Package(28m, new DateOnly(2027, 3, 31));
        var late = Package(28m, new DateOnly(2027, 12, 31));

        var allocated = PackageAllocation.Allocate([early, late], 40m);

        allocated[late.Id].Should().Be(28m);
        allocated[early.Id].Should().Be(12m);
    }

    [Fact]
    public void A_package_the_stock_no_longer_covers_is_used_up()
    {
        var early = Package(28m, new DateOnly(2027, 3, 31));
        var late = Package(28m, new DateOnly(2027, 12, 31));

        var allocated = PackageAllocation.Allocate([early, late], 20m);

        allocated[late.Id].Should().Be(20m);
        allocated[early.Id].Should().Be(0m);
    }

    [Fact]
    public void With_no_stock_every_package_is_used_up()
        => PackageAllocation.Allocate([Package(28m), Package(10m)], 0m).Values.Should().AllBeEquivalentTo(0m);

    [Fact]
    public void Untracked_stock_does_not_hide_a_tracked_package()
    {
        // 50 units in stock, only one tracked package of 28: the package
        // keeps all of it, the rest is untracked.
        var tracked = Package(28m, new DateOnly(2026, 11, 30));

        PackageAllocation.Allocate([tracked], 50m)[tracked.Id].Should().Be(28m);
    }

    [Fact]
    public void Closed_packages_are_left_out()
    {
        var closed = Package(28m, new DateOnly(2027, 12, 31));
        closed.ClosedOn = new DateOnly(2026, 9, 30);
        closed.Closure = PackageClosure.Finished;
        var open = Package(28m, new DateOnly(2027, 3, 31));

        var allocated = PackageAllocation.Allocate([closed, open], 28m);

        allocated.Should().ContainKey(open.Id).WhoseValue.Should().Be(28m);
        allocated.Should().NotContainKey(closed.Id);
    }

    [Fact]
    public void Packages_with_the_same_expiry_follow_the_order_they_were_recorded_in()
    {
        var first = Package(28m, new DateOnly(2027, 3, 31), recordedDay: 0);
        var second = Package(28m, new DateOnly(2027, 3, 31), recordedDay: 1);

        var allocated = PackageAllocation.Allocate([second, first], 30m);

        allocated[second.Id].Should().Be(28m);
        allocated[first.Id].Should().Be(2m);
    }
}
