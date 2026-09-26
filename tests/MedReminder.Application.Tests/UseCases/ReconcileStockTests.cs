using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

public class ReconcileStockTests
{
    // "Today" is 2026-09-13. A medicine started on 2026-09-10 at
    // 1 × 2/day has 3 unmaterialized days (10, 11, 12) = 6 units.
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 9, 13);

    private static async Task<Guid> SeedAsync(
        ApplicationTestScope scope,
        DateOnly? startDate = null,
        decimal initialQuantity = 30m,
        decimal dose = 1m,
        int freq = 2)
    {
        var cmd = new AddMedicineCommand(
            "Enalapril", "compresse", dose, freq,
            startDate ?? new DateOnly(2026, 9, 10), 7,
            NotificationChannels.Windows,
            InitialQuantity: initialQuantity);
        return await scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None);
    }

    private static async Task<decimal> LedgerTotalAsync(ApplicationTestScope scope, Guid id)
        => (await scope.Stock.ListForMedicineAsync(id, CancellationToken.None)).Sum(m => m.QuantityDelta);

    [Fact]
    public async Task Counted_below_expected_writes_one_negative_correction()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 20m, TakenToday: 0m, Notes: " Stock count "),
            CancellationToken.None);

        result.ExpectedQuantity.Should().Be(24m);
        result.Gap.Should().Be(-4m);
        result.CorrectionKind.Should().Be(StockMovementKind.NegativeCorrection);
        result.ConsumptionDaysMaterialized.Should().Be(3);

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        var correction = movements.Single(m =>
            m.Kind is StockMovementKind.NegativeCorrection or StockMovementKind.PositiveCorrection);
        correction.Kind.Should().Be(StockMovementKind.NegativeCorrection);
        correction.QuantityDelta.Should().Be(-4m);
        correction.Notes.Should().Be("Stock count");
        MedicineStock.Current(movements).Should().Be(20m);
    }

    [Fact]
    public async Task Counted_above_expected_writes_one_positive_correction()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 26m, TakenToday: 0m),
            CancellationToken.None);

        result.Gap.Should().Be(2m);
        result.CorrectionKind.Should().Be(StockMovementKind.PositiveCorrection);
        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        movements.Single(m => m.Kind == StockMovementKind.PositiveCorrection)
            .QuantityDelta.Should().Be(2m);
        MedicineStock.Current(movements).Should().Be(26m);
    }

    [Fact]
    public async Task Counted_equal_to_expected_writes_no_correction()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 24m, TakenToday: 0m),
            CancellationToken.None);

        result.Gap.Should().Be(0m);
        result.CorrectionKind.Should().BeNull();
        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        movements.Should().NotContain(m =>
            m.Kind == StockMovementKind.PositiveCorrection || m.Kind == StockMovementKind.NegativeCorrection);
        MedicineStock.Current(movements).Should().Be(24m);
    }

    [Fact]
    public async Task Materializes_consumption_before_computing_the_gap()
    {
        // Last catch-up on 2026-09-05; the count is taken 8 days later.
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        var id = await SeedAsync(scope, startDate: new DateOnly(2026, 9, 1), initialQuantity: 30m);
        await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        (await LedgerTotalAsync(scope, id)).Should().Be(22m);   // days 1..4

        scope.Clock.SetUtcNow(FixedNow);
        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 6m, TakenToday: 0m),
            CancellationToken.None);

        // Days 5..12 materialized: 30 - 2 × 12 = 6.
        result.ConsumptionDaysMaterialized.Should().Be(8);
        result.ExpectedQuantity.Should().Be(6m);
        result.CorrectionKind.Should().BeNull();

        // A later catch-up on the same day has nothing left to write.
        (await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Writes_materialization_and_correction_in_one_unit_of_work()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);
        var before = scope.Uow.SaveChangesCalls;

        await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 20m, TakenToday: 0m),
            CancellationToken.None);

        scope.Uow.SaveChangesCalls.Should().Be(before + 1);
    }

    [Fact]
    public async Task Taking_all_of_todays_doses_materializes_today_once()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 22m, TakenToday: 2m),
            CancellationToken.None);

        result.ExpectedQuantity.Should().Be(22m);
        result.CorrectionKind.Should().BeNull();
        result.ConsumptionDaysMaterialized.Should().Be(4);

        // Next day: the catch-up must not decrement 2026-09-13 again.
        scope.Clock.SetUtcNow(FixedNow.AddDays(1));
        (await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None)).Should().Be(0);
        (await LedgerTotalAsync(scope, id)).Should().Be(22m);
    }

    [Fact]
    public async Task Supports_decimal_units()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, initialQuantity: 10.25m, dose: 0.5m, freq: 1);

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 9.5m, TakenToday: 0m),
            CancellationToken.None);

        result.ExpectedQuantity.Should().Be(8.75m);
        result.Gap.Should().Be(0.75m);
        (await LedgerTotalAsync(scope, id)).Should().Be(9.5m);
    }

    [Fact]
    public async Task Suspended_medicine_skips_suspended_days_and_has_no_forecast()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);
        await scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 9, 11)),
            CancellationToken.None);

        var snapshot = await scope.ReconcileStock.LoadAsync(id, CancellationToken.None);
        snapshot.IsSuspendedToday.Should().BeTrue();
        snapshot.TodayScheduledQuantity.Should().Be(0m);
        var preview = snapshot.Evaluate(25m, takenToday: 0m);
        preview.ExpectedQuantity.Should().Be(28m);   // only 2026-09-10 consumed
        preview.ForecastAfter.DaysRemaining.Should().BeNull();

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 25m, TakenToday: 0m),
            CancellationToken.None);

        result.ExpectedQuantity.Should().Be(28m);
        result.Gap.Should().Be(-3m);
        (await LedgerTotalAsync(scope, id)).Should().Be(25m);
    }

    [Fact]
    public async Task Rejects_negative_counted_quantity()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);
        var countBefore = scope.Stock.All.Count;

        var act = () => scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, -1m, TakenToday: 0m),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        scope.Stock.All.Should().HaveCount(countBefore);

        var snapshot = await scope.ReconcileStock.LoadAsync(id, CancellationToken.None);
        var evaluate = () => snapshot.Evaluate(-1m, takenToday: 0m);
        evaluate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Negative_correction_does_not_change_stock_epoch()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 10m, TakenToday: 0m),
            CancellationToken.None);

        result.StockEpochAdvanced.Should().BeFalse();
        var medicine = await scope.Medicines.GetAsync(id, CancellationToken.None);
        medicine!.StockEpoch.Should().Be(1);
        (await scope.Stock.ListForMedicineAsync(id, CancellationToken.None))
            .Should().OnlyContain(m => m.StockEpoch == 1);
    }

    [Fact]
    public async Task Positive_correction_leaving_the_warning_window_advances_stock_epoch()
    {
        // 40 units at 2/day = 20 days, above the 7-day threshold.
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 40m, TakenToday: 0m),
            CancellationToken.None);

        result.StockEpochAdvanced.Should().BeTrue();
        var medicine = await scope.Medicines.GetAsync(id, CancellationToken.None);
        medicine!.StockEpoch.Should().Be(2);
        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        movements.Single(m => m.Kind == StockMovementKind.PositiveCorrection).StockEpoch.Should().Be(2);
        movements.Where(m => m.Kind == StockMovementKind.Consumption)
            .Should().OnlyContain(m => m.StockEpoch == 1);
    }

    [Fact]
    public async Task Positive_correction_inside_the_warning_window_keeps_stock_epoch()
    {
        // 10 - 6 = 4 expected; counted 6 = 3 days, still within 7.
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, initialQuantity: 10m);

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 6m, TakenToday: 0m),
            CancellationToken.None);

        result.CorrectionKind.Should().Be(StockMovementKind.PositiveCorrection);
        result.StockEpochAdvanced.Should().BeFalse();
        var medicine = await scope.Medicines.GetAsync(id, CancellationToken.None);
        medicine!.StockEpoch.Should().Be(1);
    }

    [Fact]
    public async Task Partial_intake_today_keeps_start_of_day_stock_in_the_ledger()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);

        var snapshot = await scope.ReconcileStock.LoadAsync(id, CancellationToken.None);
        var preview = snapshot.Evaluate(20m, takenToday: 1m);
        preview.ExpectedQuantity.Should().Be(23m);
        preview.Gap.Should().Be(-3m);
        preview.MaterializesToday.Should().BeFalse();
        preview.StockShownAfter.Should().Be(21m);

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 20m, TakenToday: 1m),
            CancellationToken.None);

        result.Gap.Should().Be(-3m);
        result.ConsumptionDaysMaterialized.Should().Be(3);
        (await LedgerTotalAsync(scope, id)).Should().Be(21m);

        // Next day the catch-up books today's 2 units: count minus the
        // unit that was still due.
        scope.Clock.SetUtcNow(FixedNow.AddDays(1));
        (await scope.ConsumptionCatchUp.RunAsync(CancellationToken.None)).Should().Be(1);
        (await LedgerTotalAsync(scope, id)).Should().Be(19m);
    }

    [Fact]
    public async Task Rejects_taken_today_above_scheduled_quantity()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);
        var countBefore = scope.Stock.All.Count;

        var act = () => scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 20m, TakenToday: 3m),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        scope.Stock.All.Should().HaveCount(countBefore);
    }

    [Fact]
    public async Task Suggests_taken_today_from_slots_whose_time_has_passed()
    {
        var scope = new ApplicationTestScope(FixedNow);   // 12:00 local
        var id = await scope.AddMedicine.ExecuteAsync(
            new AddMedicineCommand(
                "Enalapril", "compresse", 1m, 2,
                new DateOnly(2026, 9, 10), 7,
                NotificationChannels.Windows,
                InitialQuantity: 30m,
                AdministrationSlots:
                [
                    new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
                    new AdministrationSlotInput(1m, new TimeOnly(20, 0), null),
                ]),
            CancellationToken.None);

        var snapshot = await scope.ReconcileStock.LoadAsync(id, CancellationToken.None);

        snapshot.TodayScheduledQuantity.Should().Be(2m);
        snapshot.DefaultTakenToday.Should().Be(1m);
    }

    [Fact]
    public async Task Load_writes_nothing_and_matches_execute()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);
        var countBefore = scope.Stock.All.Count;
        var savesBefore = scope.Uow.SaveChangesCalls;

        var snapshot = await scope.ReconcileStock.LoadAsync(id, CancellationToken.None);

        scope.Stock.All.Should().HaveCount(countBefore);
        scope.Uow.SaveChangesCalls.Should().Be(savesBefore);
        snapshot.Today.Should().Be(Today);
        snapshot.LedgerAtStartOfToday.Should().Be(24m);
        snapshot.TodayScheduledQuantity.Should().Be(2m);
        snapshot.DefaultTakenToday.Should().Be(0m);
        snapshot.DailyRate.Should().Be(2m);

        var preview = snapshot.Evaluate(19m, takenToday: 2m);
        preview.ExpectedQuantity.Should().Be(22m);
        preview.Gap.Should().Be(-3m);

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 19m, TakenToday: 2m),
            CancellationToken.None);
        result.ExpectedQuantity.Should().Be(preview.ExpectedQuantity);
        result.Gap.Should().Be(preview.Gap);
    }

    [Fact]
    public async Task Forecast_after_reconciliation_matches_forecast_from_counted_quantity()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope);
        var snapshot = await scope.ReconcileStock.LoadAsync(id, CancellationToken.None);
        var preview = snapshot.Evaluate(15m, takenToday: 0m);

        await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 15m, TakenToday: 0m),
            CancellationToken.None);

        var movements = await scope.Stock.ListForMedicineAsync(id, CancellationToken.None);
        var actual = RunOutForecast.Compute(Today, MedicineStock.Current(movements), 2m, isSuspendedToday: false);
        var fromCount = RunOutForecast.Compute(Today, 15m, 2m, isSuspendedToday: false);

        actual.Should().Be(fromCount);
        preview.ForecastAfter.Should().Be(fromCount);
        fromCount.DaysRemaining.Should().Be(7);
        fromCount.EstimatedRunOutDate.Should().Be(new DateOnly(2026, 9, 20));
    }

    [Fact]
    public async Task Brings_a_negative_ledger_back_to_the_count()
    {
        // 4 units, 2/day for 3 days: raw ledger total -2, shown as 0.
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, initialQuantity: 4m);

        var preview = (await scope.ReconcileStock.LoadAsync(id, CancellationToken.None))
            .Evaluate(0m, takenToday: 0m);
        preview.Gap.Should().Be(0m);
        preview.LedgerAlignment.Should().Be(2m);

        var result = await scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 0m, TakenToday: 0m),
            CancellationToken.None);

        result.ExpectedQuantity.Should().Be(0m);
        result.Gap.Should().Be(0m);
        result.Correction.Should().Be(2m);
        result.CorrectionKind.Should().Be(StockMovementKind.PositiveCorrection);
        result.StockEpochAdvanced.Should().BeFalse();
        (await LedgerTotalAsync(scope, id)).Should().Be(0m);
    }
}
