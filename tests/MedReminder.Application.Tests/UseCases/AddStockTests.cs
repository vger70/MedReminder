using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

public class AddStockTests
{
    private static async Task<Guid> SeedMedicineAsync(ApplicationTestScope scope, decimal initial = 30m)
    {
        var cmd = new AddMedicineCommand(
            Name: "Enalapril",
            Unit: "compresse",
            DosePerAdministration: 1m,
            AdministrationsPerDay: 2,
            StartDate: new DateOnly(2026, 9, 1),
            ThresholdDays: 7,
            NotificationChannels: NotificationChannels.Windows,
            InitialQuantity: initial);
        return await scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None);
    }

    [Fact]
    public async Task Adding_new_package_increments_epoch_and_records_movement()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedMedicineAsync(scope);

        await scope.AddStock.ExecuteAsync(
            new AddStockCommand(id, 30m, StockMovementKind.NewPackage),
            CancellationToken.None);

        var medicine = await scope.Medicines.GetAsync(id, CancellationToken.None);
        medicine!.StockEpoch.Should().Be(2);

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        movements.Should().HaveCount(2);
        var added = movements.Single(m => m.Kind == StockMovementKind.NewPackage);
        added.QuantityDelta.Should().Be(30m);
        added.StockEpoch.Should().Be(2);
    }

    [Fact]
    public async Task Rejects_non_positive_quantity()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedMedicineAsync(scope);

        await FluentActions.Awaiting(() =>
            scope.AddStock.ExecuteAsync(
                new AddStockCommand(id, 0m, StockMovementKind.NewPackage),
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Rejects_negative_kinds()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedMedicineAsync(scope);

        await FluentActions.Awaiting(() =>
            scope.AddStock.ExecuteAsync(
                new AddStockCommand(id, 1m, StockMovementKind.NegativeCorrection),
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();

        await FluentActions.Awaiting(() =>
            scope.AddStock.ExecuteAsync(
                new AddStockCommand(id, 1m, StockMovementKind.Consumption),
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Fails_for_missing_medicine()
    {
        var scope = new ApplicationTestScope();

        await FluentActions.Awaiting(() =>
            scope.AddStock.ExecuteAsync(
                new AddStockCommand(Guid.NewGuid(), 5m, StockMovementKind.NewPackage),
                CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
