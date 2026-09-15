using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

public class RegisterIntakeTests
{
    private static async Task<Guid> SeedAsync(ApplicationTestScope scope, decimal initial = 30m)
    {
        var cmd = new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 2,
            new DateOnly(2026, 9, 1), 7,
            NotificationChannels.Windows,
            InitialQuantity: initial);
        return await scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None);
    }

    [Fact]
    public async Task Taken_intake_creates_consumption_movement()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        var today = new DateOnly(2026, 9, 13);

        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, today, IntakeStatus.Taken, Quantity: 1m),
            CancellationToken.None);

        var intake = scope.Intakes.All.Single();
        intake.Status.Should().Be(IntakeStatus.Taken);
        intake.Day.Should().Be(today);
        intake.ActualAt.Should().NotBeNull();

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        var consumption = movements.Single(m => m.Kind == StockMovementKind.Consumption);
        consumption.QuantityDelta.Should().Be(-1m);
    }

    [Fact]
    public async Task Skipped_intake_records_row_but_no_stock_movement()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        var today = new DateOnly(2026, 9, 13);

        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, today, IntakeStatus.Skipped, Quantity: 1m),
            CancellationToken.None);

        scope.Intakes.All.Single().Status.Should().Be(IntakeStatus.Skipped);
        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        movements.Should().NotContain(m => m.Kind == StockMovementKind.Consumption);
    }

    [Fact]
    public async Task Taken_intake_that_would_empty_stock_is_rejected()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope, initial: 0.5m);
        var today = new DateOnly(2026, 9, 13);

        await FluentActions.Awaiting(() => scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, today, IntakeStatus.Taken, Quantity: 1m),
            CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ConsumptionCatchUp_skips_days_covered_by_a_manual_intake()
    {
        // Fissiamo "oggi" al 13/9. Il catch-up materializza fino a
        // 12/9 incluso. Registriamo un'assunzione Skipped per l'11/9:
        // il catch-up deve saltare l'11/9 e materializzare solo 10 e 12.
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        var id = await SeedAsync(scope);

        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, new DateOnly(2026, 9, 11), IntakeStatus.Skipped, Quantity: 1m),
            CancellationToken.None);

        var created = await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        // Range 1..12 settembre (StartDate 1/9), 12 giorni. Il 11/9 è
        // saltato → 11 movimenti Consumption creati dal catch-up.
        created.Should().Be(11);

        var consumptionDays = (await scope.Stock.ListForMedicineAsync(id, CancellationToken.None))
            .Where(m => m.Kind == StockMovementKind.Consumption)
            .Select(m => DateOnly.FromDateTime(m.OccurredAt.DateTime))
            .OrderBy(d => d)
            .ToList();
        consumptionDays.Should().NotContain(new DateOnly(2026, 9, 11));
    }
}
