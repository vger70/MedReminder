using FluentAssertions;
using MedReminder.Application.Packages;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Packages;

public class PackageUseCaseTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);
    private readonly ApplicationTestScope _scope = new();
    private readonly Guid _medicine;

    public PackageUseCaseTests()
    {
        _medicine = _scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Timolol", Unit: "ml", DosePerAdministration: 0.1m, AdministrationsPerDay: 2,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows, EndDate: null,
            InitialQuantity: 30m), default).GetAwaiter().GetResult();
    }

    private SaveStockPackageCommand Command(Guid? id = null, decimal quantity = 5m, string? batch = null,
        DateOnly? closedOn = null, PackageClosure? closure = null)
        => new(id, _medicine, quantity, new DateOnly(2027, 3, 31), 28, Today, batch, closedOn, closure);

    private async Task<decimal> StockAsync()
        => MedicineStock.Current(await _scope.Stock.ListForMedicineAsync(_medicine, default));

    [Fact]
    public async Task A_new_package_is_recorded_with_its_dates()
    {
        var id = await _scope.SaveStockPackage.ExecuteAsync(Command(batch: "  L2345 "), default);

        var package = _scope.Packages.All.Should().ContainSingle().Subject;
        package.Id.Should().Be(id);
        package.MedicineId.Should().Be(_medicine);
        package.ExpiresOn.Should().Be(new DateOnly(2027, 3, 31));
        package.UseWithinDays.Should().Be(28);
        package.OpenedOn.Should().Be(Today);
        package.Batch.Should().Be("L2345");
        package.IsClosed.Should().BeFalse();
    }

    [Fact]
    public async Task A_package_is_replicated_as_one_operation()
    {
        _scope.EnableSync();

        var id = await _scope.SaveStockPackage.ExecuteAsync(Command(), default);

        var operation = (await _scope.SyncOperations.ListAllAsync(default))
            .Should().ContainSingle(o => o.Type == "PackageChanged").Subject;
        operation.SchemaVersion.Should().Be(11);
        operation.EntityId.Should().Be(id);
    }

    [Fact]
    public async Task An_invalid_package_is_refused()
    {
        var act = () => _scope.SaveStockPackage.ExecuteAsync(Command(quantity: 0m), default);

        (await act.Should().ThrowAsync<InvalidStockPackageException>()).Which.Error.Should().Be(PackageError.Quantity);
        _scope.Packages.All.Should().BeEmpty();
    }

    [Fact]
    public async Task A_package_cannot_move_to_another_medicine()
    {
        var id = await _scope.SaveStockPackage.ExecuteAsync(Command(), default);
        var other = await _scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Other", Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows), default);

        var act = () => _scope.SaveStockPackage.ExecuteAsync(Command(id) with { MedicineId = other }, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Finishing_a_package_closes_it_without_moving_the_stock()
    {
        var id = await _scope.SaveStockPackage.ExecuteAsync(Command(), default);

        await _scope.SaveStockPackage.ExecuteAsync(
            Command(id, closedOn: Today, closure: PackageClosure.Finished), default);

        var package = _scope.Packages.All.Should().ContainSingle().Subject;
        package.ClosedOn.Should().Be(Today);
        package.Closure.Should().Be(PackageClosure.Finished);
        (await StockAsync()).Should().Be(30m);
    }

    [Fact]
    public async Task Discarding_a_package_closes_it_and_removes_what_was_left()
    {
        var id = await _scope.SaveStockPackage.ExecuteAsync(Command(), default);

        await _scope.DiscardStockPackage.ExecuteAsync(new DiscardStockPackageCommand(id, Today, 4m), default);

        var package = _scope.Packages.All.Should().ContainSingle().Subject;
        package.Closure.Should().Be(PackageClosure.Discarded);
        package.ClosedOn.Should().Be(Today);
        (await StockAsync()).Should().Be(26m);
        _scope.Stock.All.Should().Contain(m => m.Kind == StockMovementKind.NegativeCorrection && m.QuantityDelta == -4m);
    }

    [Fact]
    public async Task Discarding_an_empty_package_leaves_the_stock_alone()
    {
        var id = await _scope.SaveStockPackage.ExecuteAsync(Command(), default);

        await _scope.DiscardStockPackage.ExecuteAsync(new DiscardStockPackageCommand(id, Today, 0m), default);

        _scope.Packages.All.Single().Closure.Should().Be(PackageClosure.Discarded);
        (await StockAsync()).Should().Be(30m);
    }

    [Fact]
    public async Task A_discard_beyond_the_stock_is_refused_and_leaves_the_package_open()
    {
        var id = await _scope.SaveStockPackage.ExecuteAsync(Command(), default);

        var act = () => _scope.DiscardStockPackage.ExecuteAsync(new DiscardStockPackageCommand(id, Today, 31m), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _scope.Packages.All.Single().IsClosed.Should().BeFalse();
        (await StockAsync()).Should().Be(30m);
    }

    [Fact]
    public async Task Deleting_a_package_leaves_the_stock_alone()
    {
        var id = await _scope.SaveStockPackage.ExecuteAsync(Command(), default);

        await _scope.DeleteStockPackage.ExecuteAsync(id, default);

        _scope.Packages.All.Should().BeEmpty();
        (await StockAsync()).Should().Be(30m);
    }
}
