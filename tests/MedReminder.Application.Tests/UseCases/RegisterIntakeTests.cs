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
        // Sep 12 included. Record a Skipped intake for Sep 11:
        // the catch-up must skip Sep 11 and materialize only 10
        // and 12.
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

    [Fact]
    public async Task Backdated_taken_intake_replaces_the_automatic_consumption()
    {
        // StartDate 1/9, today 13/9: the catch-up books 12 days x 2.
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, new DateOnly(2026, 9, 11), IntakeStatus.Taken, Quantity: 1m),
            CancellationToken.None);

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        movements.Should().ContainSingle(m => m.Kind == StockMovementKind.PositiveCorrection)
            .Which.QuantityDelta.Should().Be(2m);
        MedicineStock.Current(movements).Should().Be(30m - 24m + 2m - 1m);
    }

    [Fact]
    public async Task Backdated_skipped_intake_reverses_the_automatic_consumption()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, new DateOnly(2026, 9, 11), IntakeStatus.Skipped, Quantity: 1m),
            CancellationToken.None);

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        MedicineStock.Current(movements).Should().Be(30m - 24m + 2m);
    }

    [Fact]
    public async Task Second_intake_on_the_same_day_does_not_reverse_again()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        var day = new DateOnly(2026, 9, 11);

        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, day, IntakeStatus.Taken, Quantity: 1m),
            CancellationToken.None);
        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, day, IntakeStatus.Taken, Quantity: 1m),
            CancellationToken.None);

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        movements.Count(m => m.Kind == StockMovementKind.PositiveCorrection).Should().Be(1);
        MedicineStock.Current(movements).Should().Be(30m - 24m + 2m - 1m - 1m);
    }

    [Fact]
    public async Task Intake_for_today_writes_no_correction()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        await scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, new DateOnly(2026, 9, 13), IntakeStatus.Taken, Quantity: 1m),
            CancellationToken.None);

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        movements.Should().NotContain(m => m.Kind == StockMovementKind.PositiveCorrection);
    }
}
