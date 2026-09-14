using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

public class AdjustStockDownTests
{
    private static async Task<Guid> SeedAsync(ApplicationTestScope scope, decimal initial)
    {
        var cmd = new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 2,
            new DateOnly(2026, 9, 1), 7,
            NotificationChannels.Windows,
            InitialQuantity: initial);
        return await scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None);
    }

    [Fact]
    public async Task Creates_negative_correction_movement_without_epoch_change()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, initial: 20m);

        await scope.AdjustStockDown.ExecuteAsync(
            new AdjustStockDownCommand(id, 5m, Notes: "rovinata"),
            CancellationToken.None);

        var medicine = await scope.Medicines.GetAsync(id, CancellationToken.None);
        medicine!.StockEpoch.Should().Be(1);

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        var negative = movements.Single(m => m.Kind == StockMovementKind.NegativeCorrection);
        negative.QuantityDelta.Should().Be(-5m);
        negative.Notes.Should().Be("rovinata");
    }

    [Fact]
    public async Task Rejects_correction_that_would_make_stock_negative()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, initial: 3m);

        await FluentActions.Awaiting(() =>
            scope.AdjustStockDown.ExecuteAsync(new AdjustStockDownCommand(id, 4m), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Rejects_zero_quantity()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, initial: 3m);

        await FluentActions.Awaiting(() =>
            scope.AdjustStockDown.ExecuteAsync(new AdjustStockDownCommand(id, 0m), CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }
}
