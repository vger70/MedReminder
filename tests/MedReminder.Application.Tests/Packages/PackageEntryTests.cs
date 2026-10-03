using FluentAssertions;
using MedReminder.Application.Overview;
using MedReminder.Application.Packages;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Packages;

// Packages entered with a new-package load, the package list and the
// next expiry of the main list (docs/analysis/ANALYSIS-PACKAGE-EXPIRY.md
// §5.1, §5.3, §5.4).
public class PackageEntryTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);
    private readonly ApplicationTestScope _scope = new();
    private readonly Guid _medicine;

    public PackageEntryTests()
    {
        _medicine = AddMedicineAsync("Timolol").GetAwaiter().GetResult();
    }

    private Task<Guid> AddMedicineAsync(string name) => _scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
        Name: name, Unit: "ml", DosePerAdministration: 0.1m, AdministrationsPerDay: 2,
        StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows, EndDate: null,
        InitialQuantity: 30m), default);

    private async Task<decimal> StockAsync()
        => MedicineStock.Current(await _scope.Stock.ListForMedicineAsync(_medicine, default));

    [Fact]
    public async Task A_new_package_load_records_its_packages_linked_to_the_movement()
    {
        _scope.EnableSync();

        await _scope.AddStock.ExecuteAsync(new AddStockCommand(_medicine, 10m, StockMovementKind.NewPackage,
            Packages: new NewPackagesInput(2, new DateOnly(2027, 3, 31), 28, OpenedToday: true, " L77 ")), default);

        (await StockAsync()).Should().Be(40m);
        var movement = _scope.Stock.All.Single(m => m.Kind == StockMovementKind.NewPackage);
        var packages = _scope.Packages.All.OrderBy(p => p.RecordedAt).ToList();
        packages.Should().HaveCount(2);
        packages.Should().AllSatisfy(p =>
        {
            p.MovementId.Should().Be(movement.Id);
            p.Quantity.Should().Be(5m);
            p.ExpiresOn.Should().Be(new DateOnly(2027, 3, 31));
            p.UseWithinDays.Should().Be(28);
            p.Batch.Should().Be("L77");
        });
        packages[0].OpenedOn.Should().Be(Today);
        packages[1].OpenedOn.Should().BeNull();
        (await _scope.SyncOperations.ListAllAsync(default)).Count(o => o.Type == "PackageChanged").Should().Be(2);
    }

    [Fact]
    public async Task A_load_without_packages_records_none()
    {
        await _scope.AddStock.ExecuteAsync(new AddStockCommand(_medicine, 10m, StockMovementKind.NewPackage), default);

        _scope.Packages.All.Should().BeEmpty();
        (await StockAsync()).Should().Be(40m);
    }

    [Fact]
    public async Task An_invalid_package_refuses_the_whole_load()
    {
        var act = () => _scope.AddStock.ExecuteAsync(new AddStockCommand(_medicine, 10m, StockMovementKind.NewPackage,
            Packages: new NewPackagesInput(1, null, 400, false, null)), default);

        (await act.Should().ThrowAsync<InvalidStockPackageException>()).Which.Error.Should().Be(PackageError.UseWithinDays);
        _scope.Packages.All.Should().BeEmpty();
        (await StockAsync()).Should().Be(30m);
    }

    [Fact]
    public async Task Packages_come_only_with_a_new_package_load()
    {
        var act = () => _scope.AddStock.ExecuteAsync(new AddStockCommand(_medicine, 10m, StockMovementKind.ManualAdd,
            Packages: new NewPackagesInput(1, new DateOnly(2027, 3, 31), null, false, null)), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task The_list_shows_the_packages_to_act_on_first_and_those_the_stock_no_longer_covers_as_used_up()
    {
        // 30 in stock, consumed first expiring first: the latest package
        // holds 28, the expired one the last 2, the oldest one nothing.
        var usedUp = await SaveAsync(10m, new DateOnly(2026, 7, 31));
        var expired = await SaveAsync(2m, new DateOnly(2026, 8, 31));
        var valid = await SaveAsync(28m, new DateOnly(2027, 12, 31));

        var items = await _scope.PackageList.LoadAsync(_medicine, default);

        items.Select(i => (i.Package.Id, i.Status)).Should().Equal(
            (expired, PackageExpiryStatus.Expired),
            (valid, PackageExpiryStatus.Valid),
            (usedUp, PackageExpiryStatus.UsedUp));
        items.Single(i => i.Package.Id == valid).Allocated.Should().Be(28m);
        items.Single(i => i.Package.Id == expired).Allocated.Should().Be(2m);
    }

    [Fact]
    public async Task A_new_package_starts_from_the_latest_one()
    {
        (await _scope.PackageList.NewPackageDefaultsAsync(_medicine, default))
            .Should().Be(new NewPackageDefaults(null, null));

        await _scope.SaveStockPackage.ExecuteAsync(new SaveStockPackageCommand(
            null, _medicine, 5m, null, 28, null, null), default);
        _scope.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await _scope.SaveStockPackage.ExecuteAsync(new SaveStockPackageCommand(
            null, _medicine, 2.5m, null, 30, null, null), default);

        (await _scope.PackageList.NewPackageDefaultsAsync(_medicine, default))
            .Should().Be(new NewPackageDefaults(30, 2.5m));
    }

    [Fact]
    public async Task The_main_list_shows_the_next_expiry_with_its_status_in_words()
    {
        var other = await AddMedicineAsync("Other");
        await SaveAsync(28m, new DateOnly(2026, 8, 31));
        await SaveAsync(2m, new DateOnly(2027, 12, 31));
        var loc = new JsonDictionaryLocalizationService("en");
        var loader = new MedicineOverviewLoader(_scope.Medicines, _scope.Stock, _scope.Schedules, _scope.Suspensions,
            _scope.Slots, _scope.Clock, loc, packages: _scope.Packages);

        var rows = await loader.LoadAsync(default);

        var row = rows.Single(r => r.Id == _medicine);
        row.NextExpiry.Should().Be(new DateOnly(2026, 8, 31));
        row.NextExpiryStatus.Should().Be(PackageExpiryStatus.Expired);
        row.ExpiryDisplay.Should().Be(loc.Get("Packages.Expiry.Expired",
            new DateOnly(2026, 8, 31).ToString("d", loc.CurrentCulture)));
        row.ExpiryDisplay.Should().Contain("expired");
        rows.Single(r => r.Id == other).ExpiryDisplay.Should().BeEmpty();
    }

    private async Task<Guid> SaveAsync(decimal quantity, DateOnly expiresOn)
    {
        _scope.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        return await _scope.SaveStockPackage.ExecuteAsync(new SaveStockPackageCommand(
            null, _medicine, quantity, expiresOn, null, null, null), default);
    }
}
