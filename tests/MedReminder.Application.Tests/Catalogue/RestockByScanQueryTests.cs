using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

public sealed class RestockByScanQueryTests
{
    private const string Aic = "012745093";

    private static RestockByScanQuery NewQuery(ApplicationTestScope scope) => new(scope.Medicines, scope.Stock);

    private static Task<Guid> SeedAsync(
        ApplicationTestScope scope, string name, string? nationalCode, decimal initialQuantity = 0m)
        => scope.AddMedicine.ExecuteAsync(
            new AddMedicineCommand(
                name, "tablets", 1m, 1,
                new DateOnly(2026, 9, 1), 7,
                NotificationChannels.Windows,
                InitialQuantity: initialQuantity,
                NationalCode: nationalCode),
            CancellationToken.None);

    private static Task AddStockAsync(ApplicationTestScope scope, Guid id, decimal quantity, StockMovementKind kind)
        => scope.AddStock.ExecuteAsync(new AddStockCommand(id, quantity, kind), CancellationToken.None);

    [Fact]
    public async Task No_medicine_with_the_code_yields_no_candidate()
    {
        var scope = new ApplicationTestScope();
        await SeedAsync(scope, "Enalapril", "034567892");
        await SeedAsync(scope, "Ramipril", null);

        var candidates = await NewQuery(scope).FindByNationalCodeAsync(Aic, CancellationToken.None);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task One_medicine_with_the_code_is_found()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, "Enalapril", Aic);
        await SeedAsync(scope, "Ramipril", "034567892");

        var candidates = await NewQuery(scope).FindByNationalCodeAsync($" {Aic} ", CancellationToken.None);

        candidates.Should().ContainSingle().Which.MedicineId.Should().Be(id);
    }

    [Fact]
    public async Task Several_medicines_with_the_code_are_found_active_first()
    {
        var scope = new ApplicationTestScope();
        var inactive = await SeedAsync(scope, "A - old therapy", Aic);
        var active = await SeedAsync(scope, "B - current therapy", Aic);
        await scope.DeactivateMedicine.ExecuteAsync(new DeactivateMedicineCommand(inactive), CancellationToken.None);

        var candidates = await NewQuery(scope).FindByNationalCodeAsync(Aic, CancellationToken.None);

        candidates.Select(c => c.MedicineId).Should().Equal(active, inactive);
        candidates[1].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Quantity_is_null_without_a_new_package()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, "Enalapril", Aic, initialQuantity: 28m);
        await AddStockAsync(scope, id, 5m, StockMovementKind.ManualAdd);

        var candidate = (await NewQuery(scope).FindByNationalCodeAsync(Aic, CancellationToken.None)).Single();

        candidate.LastNewPackageQuantity.Should().BeNull();
    }

    [Fact]
    public async Task Quantity_is_the_latest_new_package_ignoring_other_kinds()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, "Enalapril", Aic);
        await AddStockAsync(scope, id, 28m, StockMovementKind.NewPackage);
        scope.Clock.AdvanceBy(TimeSpan.FromDays(20));
        await AddStockAsync(scope, id, 14m, StockMovementKind.NewPackage);
        scope.Clock.AdvanceBy(TimeSpan.FromDays(1));
        await AddStockAsync(scope, id, 3m, StockMovementKind.PositiveCorrection);

        var candidate = (await NewQuery(scope).FindByNationalCodeAsync(Aic, CancellationToken.None)).Single();

        candidate.LastNewPackageQuantity.Should().Be(14m);
    }

    [Fact]
    public async Task Unlinked_lists_only_medicines_without_a_code()
    {
        var scope = new ApplicationTestScope();
        await SeedAsync(scope, "Enalapril", Aic);
        var unlinked = await SeedAsync(scope, "Vitamin D", null);

        var candidates = await NewQuery(scope).ListUnlinkedAsync(CancellationToken.None);

        candidates.Should().ContainSingle().Which.MedicineId.Should().Be(unlinked);
    }
}
