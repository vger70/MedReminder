using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

// B.1 Phase 2b: every movement writer labels its rows. User facts are
// what the user asserted; everything computed from another fact is
// Derived (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §3.4, §4.2).
public class StockMovementOriginTests
{
    private readonly ApplicationTestScope _scope = new();
    private Guid _id;

    private async Task<IReadOnlyList<StockMovement>> NewRowsAsync(Func<Task> action)
    {
        var before = _scope.Stock.All.Select(m => m.Id).ToHashSet();
        await action();
        return _scope.Stock.All.Where(m => !before.Contains(m.Id)).ToList();
    }

    private Task SeedAsync()
        => NewRowsAsync(async () => _id = await _scope.AddMedicine.ExecuteAsync(
            new AddMedicineCommand(
                "Enalapril", "compresse", 1m, 1,
                new DateOnly(2026, 9, 1), 7,
                NotificationChannels.Windows,
                InitialQuantity: 30m),
            CancellationToken.None));

    [Fact]
    public async Task Initial_load_is_a_user_fact()
    {
        var rows = await NewRowsAsync(SeedAsync);

        rows.Should().ContainSingle()
            .Which.Should().Match<StockMovement>(m =>
                m.Kind == StockMovementKind.InitialLoad && m.Origin == StockMovementOrigin.User);
    }

    [Fact]
    public async Task Stock_entries_and_downward_adjustments_are_user_facts()
    {
        await SeedAsync();

        var added = await NewRowsAsync(() => _scope.AddStock.ExecuteAsync(
            new AddStockCommand(_id, 28m, StockMovementKind.NewPackage), CancellationToken.None));
        var adjusted = await NewRowsAsync(() => _scope.AdjustStockDown.ExecuteAsync(
            new AdjustStockDownCommand(_id, 2m), CancellationToken.None));

        added.Concat(adjusted).Should().HaveCount(2)
            .And.OnlyContain(m => m.Origin == StockMovementOrigin.User);
    }

    [Fact]
    public async Task Automatic_consumption_is_derived()
    {
        await SeedAsync();

        var rows = await NewRowsAsync(() => _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None));

        rows.Should().NotBeEmpty()
            .And.OnlyContain(m => m.Kind == StockMovementKind.Consumption
                && m.Origin == StockMovementOrigin.Derived);
    }

    [Fact]
    public async Task Intake_consumption_is_derived()
    {
        await SeedAsync();
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        var today = await NewRowsAsync(() => _scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(_id, new DateOnly(2026, 9, 13), IntakeStatus.Taken, 1m),
            CancellationToken.None));
        var backdated = await NewRowsAsync(() => _scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(_id, new DateOnly(2026, 9, 10), IntakeStatus.Taken, 1m),
            CancellationToken.None));

        today.Should().ContainSingle(m => m.Kind == StockMovementKind.Consumption);
        backdated.Should().ContainSingle(m => m.Kind == StockMovementKind.Consumption);
        today.Concat(backdated).Should().OnlyContain(m => m.Origin == StockMovementOrigin.Derived);
    }

    [Fact]
    public async Task Stock_count_correction_is_derived()
    {
        await SeedAsync();

        var rows = await NewRowsAsync(() => _scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(_id, CountedQuantity: 5m, TakenToday: 0m),
            CancellationToken.None));

        rows.Should().Contain(m => m.Kind == StockMovementKind.NegativeCorrection)
            .And.OnlyContain(m => m.Origin == StockMovementOrigin.Derived);
    }
}
