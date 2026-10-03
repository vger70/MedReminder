using FluentAssertions;
using MedReminder.Application.Packages;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Sync;

// Packages between two devices of a sync group: one register per
// package, last writer wins, deletion included; a discard moves the
// stock on both devices through its own stock entry.
public class PackageSyncTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);
    private readonly ApplicationTestScope _a = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly ApplicationTestScope _b = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, 500, TimeSpan.Zero));
    private readonly Guid _medicine;

    public PackageSyncTests()
    {
        var group = _a.EnableSync(Guid.Parse("0a000000-0000-0000-0000-000000000000"));
        _b.SyncSettingsStore.Save(group with { DeviceId = Guid.Parse("0b000000-0000-0000-0000-000000000000") });
        _medicine = _a.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Latanoprost", Unit: "ml", DosePerAdministration: 0.1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows, EndDate: null,
            InitialQuantity: 30m), default).GetAwaiter().GetResult();
    }

    private async Task ExchangeAsync()
    {
        await _b.ApplyRemote.ExecuteAsync(
            (await _a.SyncOperations.ListAllAsync(default)).Where(o => o.DeviceId != Device(_b)).ToList(), default);
        await _a.ApplyRemote.ExecuteAsync(
            (await _b.SyncOperations.ListAllAsync(default)).Where(o => o.DeviceId != Device(_a)).ToList(), default);
    }

    private static Guid Device(ApplicationTestScope scope) => scope.SyncSettingsStore.Load()!.DeviceId;

    private SaveStockPackageCommand Command(Guid? id, DateOnly? openedOn = null, Guid? medicine = null)
        => new(id, medicine ?? _medicine, 2.5m, new DateOnly(2027, 3, 31), 28, openedOn, "L77");

    [Fact]
    public async Task A_package_reaches_the_other_device()
    {
        var id = await _a.SaveStockPackage.ExecuteAsync(Command(null, Today), default);

        await ExchangeAsync();

        var copy = _b.Packages.All.Should().ContainSingle().Subject;
        copy.Id.Should().Be(id);
        copy.MedicineId.Should().Be(_medicine);
        copy.Quantity.Should().Be(2.5m);
        copy.ExpiresOn.Should().Be(new DateOnly(2027, 3, 31));
        copy.UseWithinDays.Should().Be(28);
        copy.OpenedOn.Should().Be(Today);
        copy.Batch.Should().Be("L77");
    }

    [Fact]
    public async Task Concurrent_changes_end_with_the_later_one_on_both_devices()
    {
        var id = await _a.SaveStockPackage.ExecuteAsync(Command(null), default);
        await ExchangeAsync();

        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        _b.Clock.AdvanceBy(TimeSpan.FromMinutes(2));
        await _a.SaveStockPackage.ExecuteAsync(Command(id, Today), default);
        await _b.SaveStockPackage.ExecuteAsync(
            Command(id) with { ClosedOn = Today, Closure = PackageClosure.Finished }, default);
        await ExchangeAsync();

        foreach (var scope in new[] { _a, _b })
        {
            var p = scope.Packages.All.Should().ContainSingle().Subject;
            p.OpenedOn.Should().BeNull("B's later write held the state B had");
            p.Closure.Should().Be(PackageClosure.Finished);
        }
    }

    [Fact]
    public async Task A_deletion_reaches_the_other_device()
    {
        var id = await _a.SaveStockPackage.ExecuteAsync(Command(null), default);
        await ExchangeAsync();

        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await _a.DeleteStockPackage.ExecuteAsync(id, default);
        await ExchangeAsync();

        _a.Packages.All.Should().BeEmpty();
        _b.Packages.All.Should().BeEmpty();
    }

    [Fact]
    public async Task A_discard_closes_the_package_and_moves_the_stock_on_the_other_device()
    {
        var id = await _a.SaveStockPackage.ExecuteAsync(Command(null), default);
        await ExchangeAsync();

        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await _a.DiscardStockPackage.ExecuteAsync(new DiscardStockPackageCommand(id, Today, 2m), default);
        await ExchangeAsync();

        _b.Packages.All.Single().Closure.Should().Be(PackageClosure.Discarded);
        MedicineStock.Current(await _b.Stock.ListForMedicineAsync(_medicine, default))
            .Should().Be(MedicineStock.Current(await _a.Stock.ListForMedicineAsync(_medicine, default)));
        _b.Stock.All.Should().Contain(m => m.Kind == StockMovementKind.NegativeCorrection && m.QuantityDelta == -2m);
    }

    [Fact]
    public async Task Deleting_the_medicine_removes_its_packages_on_the_other_device()
    {
        // A medicine with no recorded stock can be deleted.
        var mistake = await _a.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Mistake", Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows), default);
        await _a.SaveStockPackage.ExecuteAsync(Command(null, medicine: mistake), default);
        var own = await _a.SaveStockPackage.ExecuteAsync(Command(null), default);
        await ExchangeAsync();

        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        (await _a.DeleteMedicine.ExecuteAsync(new DeleteMedicineCommand(mistake), default))
            .Should().Be(DeleteMedicineOutcome.Deleted);
        await ExchangeAsync();

        _a.Packages.All.Should().ContainSingle().Which.Id.Should().Be(own);
        _b.Packages.All.Should().ContainSingle().Which.Id.Should().Be(own);
    }
}
