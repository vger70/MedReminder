using FluentAssertions;
using MedReminder.Application.Coverage;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Coverage;

// CoveragePlanQuery reads the active medicines through the repositories
// and takes the package size from the last new package.
public class CoveragePlanQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_plan_uses_the_current_stock_and_the_last_new_package()
    {
        var scope = new ApplicationTestScope(Now);
        var id = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Enalapril",
            Unit: "tablets",
            DosePerAdministration: 1m,
            AdministrationsPerDay: 2,
            StartDate: new DateOnly(2026, 10, 1),
            ThresholdDays: 7,
            NotificationChannels: NotificationChannels.Windows,
            EndDate: null,
            InitialQuantity: 4m), default);
        await scope.AddStock.ExecuteAsync(new AddStockCommand(id, 14m, StockMovementKind.NewPackage), default);
        var query = new CoveragePlanQuery(scope.Medicines, scope.Stock, scope.Schedules, scope.Suspensions,
            scope.Slots, scope.Clock);

        var today = query.LocalToday();
        var plan = await query.LoadAsync(today, today.AddDays(13), default);

        var row = plan.Rows.Should().ContainSingle().Subject;
        row.CurrentStock.Should().Be(18m);
        row.Needed.Should().Be(28m);
        row.Shortfall.Should().Be(10m);
        row.PackageQuantity.Should().Be(14m);
        row.Packages.Should().Be(1);
    }
}
