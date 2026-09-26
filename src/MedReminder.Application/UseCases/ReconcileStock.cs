using MedReminder.Application.Abstractions;
using MedReminder.Application.Monitoring;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.UseCases;

// Guided stock count: the user types the quantity physically counted,
// the app compares it with the expected stock and records one
// correction so that the ledger total equals the count.
//
// Expected stock = ledger total with automatic consumption materialized
// up to the moment of the count, through the same planner used by
// ConsumptionCatchUp (no second stock calculation). The catch-up stops
// at yesterday; when the count is taken after today's scheduled doses,
// today's automatic consumption is materialized as well. The catch-up
// skips a day that already carries a Consumption movement, so today is
// never decremented twice.
//
// The gap is a stock discrepancy (counted - expected). It is not
// interpreted as missed or extra doses (EVOLUTION.md §9.2).
//
// The correction does not increment StockEpoch: this is not a refill.
public sealed record ReconcileStockCommand(
    Guid MedicineId,
    decimal CountedQuantity,
    bool CountedAfterTodaysDoses,
    string? Notes = null);

public sealed record ReconcileStockResult(
    decimal ExpectedQuantity,
    decimal Gap,
    StockMovementKind? CorrectionKind,
    int ConsumptionDaysMaterialized);

// Read-only view used by the count dialog to show the gap and the
// run-out effect before the user confirms.
public sealed record StockCountSnapshot(
    Guid MedicineId,
    DateOnly Today,
    decimal LedgerAtStartOfToday,   // raw sum, may be negative
    decimal TodayScheduledQuantity, // automatic consumption still due today
    decimal DailyRate,
    bool IsSuspendedToday)
{
    public StockCountPreview Evaluate(decimal countedQuantity, bool countedAfterTodaysDoses)
    {
        if (countedQuantity < 0m)
            throw new ArgumentOutOfRangeException(nameof(countedQuantity), "Counted quantity cannot be negative.");

        var raw = LedgerAtStartOfToday - (countedAfterTodaysDoses ? TodayScheduledQuantity : 0m);
        var expected = raw < 0m ? 0m : raw;
        return new StockCountPreview(
            expected,
            countedQuantity,
            countedQuantity - expected,
            RunOutForecast.Compute(Today, expected, DailyRate, IsSuspendedToday),
            RunOutForecast.Compute(Today, countedQuantity, DailyRate, IsSuspendedToday));
    }
}

public sealed record StockCountPreview(
    decimal ExpectedQuantity,
    decimal CountedQuantity,
    decimal Gap,
    RunOutForecastResult ForecastBefore,
    RunOutForecastResult ForecastAfter);

public sealed class ReconcileStock
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly ConsumptionCatchUp _catchUp;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ReconcileStock(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IMedicationAdministrationSlotRepository slots,
        ConsumptionCatchUp catchUp,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _stock = stock;
        _schedules = schedules;
        _suspensions = suspensions;
        _slots = slots;
        _catchUp = catchUp;
        _uow = uow;
        _clock = clock;
    }

    // Writes nothing. The pending consumption is computed in memory.
    public async Task<StockCountSnapshot> LoadAsync(Guid medicineId, CancellationToken cancellationToken)
    {
        var medicine = await _medicines.GetAsync(medicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {medicineId} not found.");

        var today = _catchUp.LocalToday();
        var ledger = await _stock.ListForMedicineAsync(medicineId, cancellationToken);
        var ledgerTotal = ledger.Sum(m => m.QuantityDelta);

        var pendingBeforeToday = 0m;
        var pendingThroughToday = 0m;
        if (medicine.IsActive)
        {
            pendingBeforeToday = (await _catchUp.PlanMissingAsync(
                medicine, ledger, today.AddDays(-1), cancellationToken)).Sum(m => m.QuantityDelta);
            pendingThroughToday = (await _catchUp.PlanMissingAsync(
                medicine, ledger, today, cancellationToken)).Sum(m => m.QuantityDelta);
        }

        var schedule = await _schedules.ListForMedicineAsync(medicineId, cancellationToken);
        var slots = await _slots.ListForMedicineAsync(medicineId, cancellationToken);
        var suspensions = await _suspensions.ListForMedicineAsync(medicineId, cancellationToken);

        return new StockCountSnapshot(
            medicineId,
            today,
            ledgerTotal + pendingBeforeToday,
            pendingBeforeToday - pendingThroughToday,
            DailyConsumption.RateOn(today, schedule, slots),
            SuspensionState.IsSuspendedOn(today, suspensions));
    }

    // Runs under MonitoringGate: a catch-up committing between the
    // materialization below and the save would write the same days
    // again (double decrement) and skew the gap.
    public Task<ReconcileStockResult> ExecuteAsync(ReconcileStockCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.CountedQuantity < 0m)
            throw new ArgumentException("Counted quantity cannot be negative.", nameof(cmd));
        return MonitoringGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    private async Task<ReconcileStockResult> ExecuteCoreAsync(ReconcileStockCommand cmd, CancellationToken cancellationToken)
    {
        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        var today = _catchUp.LocalToday();
        var ledger = await _stock.ListForMedicineAsync(medicine.Id, cancellationToken);

        // Same scope as ConsumptionCatchUp.RunAsync: inactive medicines
        // are not materialized.
        IReadOnlyList<StockMovement> pending = [];
        if (medicine.IsActive)
        {
            var rangeEnd = cmd.CountedAfterTodaysDoses ? today : today.AddDays(-1);
            pending = await _catchUp.PlanMissingAsync(medicine, ledger, rangeEnd, cancellationToken);
        }

        // The correction brings the raw ledger total to the count, even
        // when consumption has pushed it below zero; the reported gap
        // uses the clamped stock the user sees.
        var raw = ledger.Sum(m => m.QuantityDelta) + pending.Sum(m => m.QuantityDelta);
        var expected = raw < 0m ? 0m : raw;
        var delta = cmd.CountedQuantity - raw;

        StockMovementKind? kind = delta switch
        {
            > 0m => StockMovementKind.PositiveCorrection,
            < 0m => StockMovementKind.NegativeCorrection,
            _ => null,
        };

        if (pending.Count > 0)
        {
            await _stock.AddRangeAsync(pending, cancellationToken);
        }
        if (kind is { } correctionKind)
        {
            await _stock.AddAsync(new StockMovement
            {
                MedicineId = medicine.Id,
                OccurredAt = _clock.GetUtcNow(),
                Kind = correctionKind,
                QuantityDelta = delta,
                StockEpoch = medicine.StockEpoch,
                Notes = string.IsNullOrWhiteSpace(cmd.Notes) ? null : cmd.Notes.Trim(),
            }, cancellationToken);
        }
        if (pending.Count > 0 || kind is not null)
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }

        return new ReconcileStockResult(expected, cmd.CountedQuantity - expected, kind, pending.Count);
    }
}
