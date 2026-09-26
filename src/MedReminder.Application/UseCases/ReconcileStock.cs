using MedReminder.Application.Abstractions;
using MedReminder.Application.Monitoring;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.UseCases;

// Guided stock count: the user types the quantity physically counted,
// the app compares it with the expected stock and records one
// correction.
//
// Stock semantics. ConsumptionCatchUp materializes consumption up to
// yesterday, so the ledger total is the stock at the start of today.
// Expected stock = that total (pending days materialized through the
// same planner as the catch-up, no second stock calculation) minus the
// part of today's scheduled consumption the user has already taken
// (TakenToday, 0..today's scheduled quantity).
//
// The correction is always counted - expected (raw). Where it lands:
//  - TakenToday = today's whole scheduled quantity: today's automatic
//    consumption is materialized now; the ledger equals the count. The
//    catch-up skips a day that already has a Consumption movement, so
//    today is never decremented twice.
//  - otherwise: today stays unmaterialized and the ledger holds the
//    start-of-day stock implied by the count (counted + TakenToday).
//    The next catch-up subtracts today's full quantity, leaving the
//    count minus the doses still due today.
//
// Ledger below zero. The catch-up keeps consuming at zero stock, so
// the raw total can be negative while the user sees 0. The gap shown
// is against the clamped value; the correction also absorbs the
// negative part (LedgerAlignment) so the ledger matches the count.
//
// StockEpoch. A positive correction advances the epoch unless the
// forecast after it is still inside the warning window: this reopens
// the low-stock warning cycle when the count lifts the stock above the
// threshold, without re-sending a warning straight away when it does
// not. A negative correction never changes the epoch.
//
// The gap is a stock discrepancy. It is not interpreted as missed or
// extra doses (EVOLUTION.md §9.2).
public sealed record ReconcileStockCommand(
    Guid MedicineId,
    decimal CountedQuantity,
    decimal TakenToday,
    string? Notes = null);

public sealed record ReconcileStockResult(
    decimal ExpectedQuantity,
    decimal Gap,
    decimal Correction,
    StockMovementKind? CorrectionKind,
    bool StockEpochAdvanced,
    int ConsumptionDaysMaterialized);

// Read-only view used by the count dialog; ExecuteAsync evaluates the
// same snapshot, so the preview and the write cannot diverge.
public sealed record StockCountSnapshot(
    Guid MedicineId,
    DateOnly Today,
    decimal LedgerAtStartOfToday,   // raw sum, may be negative
    decimal TodayScheduledQuantity, // automatic consumption still due today
    decimal DefaultTakenToday,      // slot doses whose time has passed
    decimal DailyRate,
    bool IsSuspendedToday)
{
    public StockCountPreview Evaluate(decimal countedQuantity, decimal takenToday)
    {
        if (countedQuantity < 0m)
            throw new ArgumentOutOfRangeException(nameof(countedQuantity), "Counted quantity cannot be negative.");
        if (takenToday < 0m || takenToday > TodayScheduledQuantity)
            throw new ArgumentOutOfRangeException(nameof(takenToday),
                $"Quantity taken today must be between 0 and {TodayScheduledQuantity}.");

        var rawExpected = LedgerAtStartOfToday - takenToday;
        var expected = Clamp(rawExpected);
        var correction = countedQuantity - rawExpected;
        var materializesToday = TodayScheduledQuantity > 0m && takenToday == TodayScheduledQuantity;
        var ledgerAfter = materializesToday ? countedQuantity : countedQuantity + takenToday;

        return new StockCountPreview(
            expected,
            countedQuantity,
            countedQuantity - expected,
            correction,
            correction - (countedQuantity - expected),
            materializesToday,
            ledgerAfter,
            RunOutForecast.Compute(Today, Clamp(LedgerAtStartOfToday), DailyRate, IsSuspendedToday),
            RunOutForecast.Compute(Today, ledgerAfter, DailyRate, IsSuspendedToday));
    }

    private static decimal Clamp(decimal value) => value < 0m ? 0m : value;
}

// ForecastBefore / ForecastAfter are computed on the stock the main
// list shows before and after the count (start-of-day semantics).
public sealed record StockCountPreview(
    decimal ExpectedQuantity,
    decimal CountedQuantity,
    decimal Gap,
    decimal Correction,
    decimal LedgerAlignment,
    bool MaterializesToday,
    decimal StockShownAfter,
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
        var medicine = await GetMedicineAsync(medicineId, cancellationToken);
        var ledger = await _stock.ListForMedicineAsync(medicineId, cancellationToken);
        return (await BuildStateAsync(medicine, ledger, cancellationToken)).Snapshot;
    }

    // Runs under MonitoringGate: a catch-up committing between the
    // materialization below and the save would write the same days
    // again (double decrement) and skew the gap.
    public Task<ReconcileStockResult> ExecuteAsync(ReconcileStockCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.CountedQuantity < 0m)
            throw new ArgumentException("Counted quantity cannot be negative.", nameof(cmd));
        if (cmd.TakenToday < 0m)
            throw new ArgumentException("Quantity taken today cannot be negative.", nameof(cmd));
        return MonitoringGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    private async Task<ReconcileStockResult> ExecuteCoreAsync(ReconcileStockCommand cmd, CancellationToken cancellationToken)
    {
        var medicine = await GetMedicineAsync(cmd.MedicineId, cancellationToken);
        var ledger = await _stock.ListForMedicineAsync(medicine.Id, cancellationToken);
        var state = await BuildStateAsync(medicine, ledger, cancellationToken);
        var preview = state.Snapshot.Evaluate(cmd.CountedQuantity, cmd.TakenToday);

        var pending = preview.MaterializesToday ? state.PendingThroughToday : state.PendingBeforeToday;

        StockMovementKind? kind = preview.Correction switch
        {
            > 0m => StockMovementKind.PositiveCorrection,
            < 0m => StockMovementKind.NegativeCorrection,
            _ => null,
        };
        var advanceEpoch = kind == StockMovementKind.PositiveCorrection
            && !(preview.ForecastAfter.DaysRemaining is int days && days <= medicine.ThresholdDays);

        // Pending consumption was planned with the current epoch; the
        // correction belongs to the new one, as in AddStock.
        if (pending.Count > 0)
        {
            await _stock.AddRangeAsync(pending, cancellationToken);
        }
        if (advanceEpoch)
        {
            medicine.StockEpoch += 1;
            medicine.UpdatedAt = _clock.GetUtcNow();
            await _medicines.UpdateAsync(medicine, cancellationToken);
        }
        if (kind is { } correctionKind)
        {
            await _stock.AddAsync(new StockMovement
            {
                MedicineId = medicine.Id,
                OccurredAt = _clock.GetUtcNow(),
                Kind = correctionKind,
                QuantityDelta = preview.Correction,
                StockEpoch = medicine.StockEpoch,
                Notes = string.IsNullOrWhiteSpace(cmd.Notes) ? null : cmd.Notes.Trim(),
            }, cancellationToken);
        }
        if (pending.Count > 0 || kind is not null)
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }

        return new ReconcileStockResult(
            preview.ExpectedQuantity, preview.Gap, preview.Correction, kind, advanceEpoch, pending.Count);
    }

    private async Task<Medicine> GetMedicineAsync(Guid medicineId, CancellationToken cancellationToken)
        => await _medicines.GetAsync(medicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {medicineId} not found.");

    private async Task<CountState> BuildStateAsync(
        Medicine medicine, IReadOnlyList<StockMovement> ledger, CancellationToken cancellationToken)
    {
        var today = _catchUp.LocalToday();

        // Same scope as ConsumptionCatchUp.RunAsync: inactive medicines
        // are not materialized.
        IReadOnlyList<StockMovement> pendingBefore = [];
        IReadOnlyList<StockMovement> pendingThrough = [];
        if (medicine.IsActive)
        {
            pendingBefore = await _catchUp.PlanMissingAsync(medicine, ledger, today.AddDays(-1), cancellationToken);
            pendingThrough = await _catchUp.PlanMissingAsync(medicine, ledger, today, cancellationToken);
        }
        var todayScheduled = pendingBefore.Sum(m => m.QuantityDelta) - pendingThrough.Sum(m => m.QuantityDelta);

        var schedule = await _schedules.ListForMedicineAsync(medicine.Id, cancellationToken);
        var slots = await _slots.ListForMedicineAsync(medicine.Id, cancellationToken);
        var suspensions = await _suspensions.ListForMedicineAsync(medicine.Id, cancellationToken);

        var snapshot = new StockCountSnapshot(
            medicine.Id,
            today,
            ledger.Sum(m => m.QuantityDelta) + pendingBefore.Sum(m => m.QuantityDelta),
            todayScheduled,
            DefaultTakenToday(todayScheduled, slots),
            DailyConsumption.RateOn(today, schedule, slots),
            SuspensionState.IsSuspendedOn(today, suspensions));
        return new CountState(snapshot, pendingBefore, pendingThrough);
    }

    // Sum of the doses of timed slots whose time has already passed,
    // as a suggestion only. Without slots (or without times) nothing
    // can be inferred and the suggestion is 0.
    private decimal DefaultTakenToday(decimal todayScheduled, IReadOnlyList<MedicationAdministrationSlot> slots)
    {
        if (todayScheduled <= 0m || slots.Count == 0) return 0m;
        var now = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone).DateTime);
        var taken = slots.Where(s => s.Time is { } t && t <= now).Sum(s => s.Dose);
        return Math.Min(taken, todayScheduled);
    }

    private sealed record CountState(
        StockCountSnapshot Snapshot,
        IReadOnlyList<StockMovement> PendingBeforeToday,
        IReadOnlyList<StockMovement> PendingThroughToday);
}
