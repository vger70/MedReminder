using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Tests.Support;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

public class MedicineStockTests
{
    [Fact]
    public void Current_returns_zero_when_no_movements()
    {
        MedicineStock.Current(Array.Empty<StockMovement>()).Should().Be(0m);
    }

    [Fact]
    public void Current_sums_positive_movements()
    {
        var movements = new[]
        {
            DomainFactory.Movement(StockMovementKind.InitialLoad, 30m),
            DomainFactory.Movement(StockMovementKind.NewPackage, 30m),
        };

        MedicineStock.Current(movements).Should().Be(60m);
    }

    [Fact]
    public void Current_nets_positive_and_negative_movements()
    {
        var movements = new[]
        {
            DomainFactory.Movement(StockMovementKind.InitialLoad, 30m),
            DomainFactory.Movement(StockMovementKind.Consumption, -2m),
            DomainFactory.Movement(StockMovementKind.Consumption, -2m),
        };

        MedicineStock.Current(movements).Should().Be(26m);
    }

    [Fact]
    public void Current_clamps_to_zero_when_algebraic_sum_is_negative()
    {
        var movements = new[]
        {
            DomainFactory.Movement(StockMovementKind.InitialLoad, 5m),
            DomainFactory.Movement(StockMovementKind.NegativeCorrection, -10m),
        };

        MedicineStock.Current(movements).Should().Be(0m);
    }

    [Fact]
    public void WouldGoNegative_true_when_proposed_delta_exceeds_stock()
    {
        MedicineStock.WouldGoNegative(currentStock: 10m, proposedDelta: -15m)
            .Should().BeTrue();
    }

    [Fact]
    public void WouldGoNegative_false_when_proposed_delta_leaves_non_negative_stock()
    {
        MedicineStock.WouldGoNegative(currentStock: 10m, proposedDelta: -5m)
            .Should().BeFalse();
        MedicineStock.WouldGoNegative(currentStock: 10m, proposedDelta: -10m)
            .Should().BeFalse();
        MedicineStock.WouldGoNegative(currentStock: 10m, proposedDelta: 0m)
            .Should().BeFalse();
    }

    [Fact]
    public void CurrentForEpoch_counts_only_matching_epoch_movements()
    {
        var movements = new[]
        {
            DomainFactory.Movement(StockMovementKind.InitialLoad, 30m, epoch: 1),
            DomainFactory.Movement(StockMovementKind.Consumption, -5m, epoch: 1),
            DomainFactory.Movement(StockMovementKind.NewPackage, 30m, epoch: 2),
            DomainFactory.Movement(StockMovementKind.Consumption, -3m, epoch: 2),
        };

        MedicineStock.CurrentForEpoch(movements, epoch: 1).Should().Be(25m);
        MedicineStock.CurrentForEpoch(movements, epoch: 2).Should().Be(27m);
    }
}
