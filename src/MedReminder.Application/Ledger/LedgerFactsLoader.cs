using MedReminder.Application.Abstractions;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Ledger;

// Reads a medicine's facts from the repositories into LedgerFacts
// (B.1 Phase 2c-2, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §3.5, §4.3).
// Resolves what the deriver leaves to the caller:
//   - frozen medicine: the profile has a cutoff and the medicine was
//     created before the freeze; then the cutoff day is the profile's,
//     otherwise the day before StartDate;
//   - legacy intakes: recorded before the freeze instant;
//   - counts: only those recorded after the freeze (earlier counts are
//     already Legacy rows);
//   - activity: a medicine with no recorded change that is inactive now
//     is treated as inactive after the cutoff.
public sealed class LedgerFactsLoader
{
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IMedicineActivityRepository _activity;
    private readonly IStockCountRepository _counts;
    private readonly ILedgerCutoffRepository _cutoff;

    public LedgerFactsLoader(
        IStockMovementRepository stock,
        IMedicationIntakeRepository intakes,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IMedicationAdministrationSlotRepository slots,
        IMedicineActivityRepository activity,
        IStockCountRepository counts,
        ILedgerCutoffRepository cutoff)
    {
        _stock = stock;
        _intakes = intakes;
        _schedules = schedules;
        _suspensions = suspensions;
        _slots = slots;
        _activity = activity;
        _counts = counts;
        _cutoff = cutoff;
    }

    public async Task<LedgerFacts> LoadAsync(Medicine medicine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(medicine);

        var cutoff = await _cutoff.GetAsync(cancellationToken);
        var frozenAt = cutoff?.FrozenAt;
        var frozen = cutoff is not null && medicine.CreatedAt < cutoff.FrozenAt;
        var cutoffDay = frozen ? cutoff!.CutoffDay : medicine.StartDate.AddDays(-1);

        var movements = await _stock.ListForMedicineAsync(medicine.Id, cancellationToken);
        var intakes = await _intakes.ListForMedicineAsync(medicine.Id, cancellationToken);
        var schedule = await _schedules.ListForMedicineAsync(medicine.Id, cancellationToken);
        var suspensions = await _suspensions.ListForMedicineAsync(medicine.Id, cancellationToken);
        var sets = await _slots.ListSetsForMedicineAsync(medicine.Id, cancellationToken);
        var activity = await _activity.ListForMedicineAsync(medicine.Id, cancellationToken);
        var counts = await _counts.ListForMedicineAsync(medicine.Id, cancellationToken);

        var ledgerActivity = activity
            .OrderBy(a => a.RecordedAt)
            .Select(a => new LedgerActivity(a.Day, a.Active, a.RecordedAt))
            .ToList();
        if (ledgerActivity.Count == 0 && !medicine.IsActive)
        {
            ledgerActivity.Add(new LedgerActivity(cutoffDay.AddDays(1), false, DateTimeOffset.MinValue));
        }

        return new LedgerFacts
        {
            MedicineId = medicine.Id,
            StartDate = medicine.StartDate,
            EndDate = medicine.EndDate,
            CutoffDay = cutoffDay,
            BaselineEpoch = frozen ? medicine.LedgerBaselineEpoch : 1,
            LegacyMovements = movements.Where(m => m.Origin == StockMovementOrigin.Legacy).ToList(),
            UserEntries = movements.Where(m => m.Origin == StockMovementOrigin.User).ToList(),
            Intakes = intakes
                .OrderBy(i => i.RecordedAt)
                .Select(i => ToLedger(i, frozenAt))
                .ToList(),
            Schedule = schedule.OrderBy(s => s.RecordedAt).ToList(),
            Suspensions = suspensions,
            SlotSets = sets
                .Select(e => new LedgerSlotSet(e.Set.EffectiveFrom, e.Set.RecordedAt, e.Slots))
                .ToList(),
            Activity = ledgerActivity,
            Counts = counts
                .Where(c => frozenAt is not { } f || c.RecordedAt >= f)
                .OrderBy(c => c.RecordedAt)
                .Select(ToAnchor)
                .ToList(),
        };
    }

    public static LedgerIntake ToLedger(MedicationIntake intake, DateTimeOffset? frozenAt)
        => new(
            intake.Id,
            intake.Day,
            intake.Status,
            intake.Quantity,
            intake.RecordedAt,
            IsLegacy: frozenAt is { } f && intake.RecordedAt < f,
            intake.Notes);

    public static StockCountAnchor ToAnchor(StockCount c)
        => new(
            c.Id, c.CountDay, c.RecordedAt, c.CountedQuantity, c.TakenToday, c.ThresholdAtCount,
            c.LedgerAtStartOfDay, c.CountDayScheduled, c.Correction, c.MaterializesCountDay,
            c.AdvancesEpoch, c.Notes);
}
