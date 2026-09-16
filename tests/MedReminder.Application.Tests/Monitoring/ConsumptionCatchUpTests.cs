using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Monitoring;

public class ConsumptionCatchUpTests
{
    // Il monitor materializza i consumi giornalieri fino a "ieri" incluso.
    // I test fissano "oggi" al 2026-09-13; i giorni attesi vanno quindi
    // dalla StartDate al 2026-09-12.
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedAsync(
        ApplicationTestScope scope,
        DateOnly startDate,
        decimal initialQuantity = 30m,
        decimal dose = 1m,
        int freq = 2)
    {
        var cmd = new AddMedicineCommand(
            "Enalapril", "compresse", dose, freq,
            startDate, 7,
            NotificationChannels.Windows,
            InitialQuantity: initialQuantity);
        return await scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None);
    }

    [Fact]
    public async Task Materializes_daily_consumption_up_to_yesterday()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, new DateOnly(2026, 9, 10));

        var created = await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        // Days: 10, 11, 12 = 3 giorni.
        created.Should().Be(3);
        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        movements.Where(m => m.Kind == StockMovementKind.Consumption)
            .Select(m => DateOnly.FromDateTime(m.OccurredAt.DateTime))
            .Should().Equal(
                new DateOnly(2026, 9, 10),
                new DateOnly(2026, 9, 11),
                new DateOnly(2026, 9, 12));
        movements.Where(m => m.Kind == StockMovementKind.Consumption)
            .Should().OnlyContain(m => m.QuantityDelta == -2m);
    }

    [Fact]
    public async Task Second_call_is_idempotent()
    {
        var scope = new ApplicationTestScope(FixedNow);
        _ = await SeedAsync(scope, new DateOnly(2026, 9, 10));

        var first = await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        var second = await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        first.Should().Be(3);
        second.Should().Be(0);
    }

    [Fact]
    public async Task Skips_days_within_a_closed_suspension()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, new DateOnly(2026, 9, 10));
        await scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 9, 11)),
            CancellationToken.None);
        await scope.ResumeMedication.ExecuteAsync(
            new ResumeMedicationCommand(id, new DateOnly(2026, 9, 12)),
            CancellationToken.None);

        var created = await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        // Giorni 10, 11, 12 → 11 e 12 sospesi → resta solo il 10.
        created.Should().Be(1);
    }

    [Fact]
    public async Task Reflects_schedule_change_mid_range()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, new DateOnly(2026, 9, 10), dose: 1m, freq: 2);

        await scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 1m, 3, new DateOnly(2026, 9, 12)),
            CancellationToken.None);

        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        var consumptions = (await scope.Stock.ListForMedicineAsync(id, CancellationToken.None))
            .Where(m => m.Kind == StockMovementKind.Consumption)
            .OrderBy(m => m.OccurredAt)
            .ToList();

        consumptions.Should().HaveCount(3);
        consumptions[0].QuantityDelta.Should().Be(-2m);   // 10/9
        consumptions[1].QuantityDelta.Should().Be(-2m);   // 11/9
        consumptions[2].QuantityDelta.Should().Be(-3m);   // 12/9 (nuova schedule)
    }

    [Fact]
    public async Task Current_stock_after_catch_up_matches_expected_balance()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, new DateOnly(2026, 9, 10), initialQuantity: 30m);

        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        MedicineStock.Current(movements).Should().Be(30m - (3 * 2m));  // 24 compresse
    }

    [Fact]
    public async Task Does_nothing_when_range_start_is_after_yesterday()
    {
        var scope = new ApplicationTestScope(FixedNow);
        _ = await SeedAsync(scope, new DateOnly(2026, 9, 14));

        var created = await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        created.Should().Be(0);
    }
}
