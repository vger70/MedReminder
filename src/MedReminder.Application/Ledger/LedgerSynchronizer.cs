using MedReminder.Application.Abstractions;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Ledger;

public sealed record LedgerSyncResult(
    DerivedLedger Ledger,
    int RowsAdded,
    int RowsChanged,
    int RowsRemoved,
    bool EpochChanged);

// Derives a medicine's ledger and makes the stored Derived rows and
// Medicine.StockEpoch match it (B.1 Phase 2c-2, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §4.3). Rows are compared by their
// deterministic id, so an unchanged ledger writes nothing. Does not
// call SaveChangesAsync: the caller owns the unit of work and must hold
// MonitoringGate, like every other ledger writer.
public sealed class LedgerSynchronizer
{
    private readonly LedgerFactsLoader _loader;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicineRepository _medicines;
    private readonly TimeProvider _clock;

    public LedgerSynchronizer(
        LedgerFactsLoader loader,
        IStockMovementRepository stock,
        IMedicineRepository medicines,
        TimeProvider clock)
    {
        _loader = loader;
        _stock = stock;
        _medicines = medicines;
        _clock = clock;
    }

    public TimeZoneInfo Zone => _clock.LocalTimeZone;

    public DateOnly LocalToday()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), Zone).DateTime);

    public Task<LedgerFacts> LoadFactsAsync(Medicine medicine, CancellationToken cancellationToken)
        => _loader.LoadAsync(medicine, cancellationToken);

    public async Task<LedgerSyncResult> SynchronizeAsync(Medicine medicine, CancellationToken cancellationToken)
        => await ApplyAsync(medicine, await _loader.LoadAsync(medicine, cancellationToken), cancellationToken);

    // `facts` may include facts added in the current unit of work and
    // not saved yet.
    public async Task<LedgerSyncResult> ApplyAsync(
        Medicine medicine, LedgerFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        ArgumentNullException.ThrowIfNull(facts);

        var ledger = LedgerDeriver.Derive(facts, LocalToday(), Zone);

        var stored = (await _stock.ListForMedicineAsync(medicine.Id, cancellationToken))
            .Where(m => m.Origin == StockMovementOrigin.Derived)
            .ToDictionary(m => m.Id);
        var desired = ledger.DerivedRows.Select(r => ToMovement(medicine.Id, r)).ToList();
        var desiredIds = desired.Select(m => m.Id).ToHashSet();

        var toAdd = new List<StockMovement>();
        var toUpdate = new List<StockMovement>();
        foreach (var movement in desired)
        {
            if (!stored.TryGetValue(movement.Id, out var current)) toAdd.Add(movement);
            else if (!SameContent(current, movement)) toUpdate.Add(movement);
        }
        var toRemove = stored.Values.Where(m => !desiredIds.Contains(m.Id)).ToList();

        if (toRemove.Count > 0) await _stock.RemoveRangeAsync(toRemove, cancellationToken);
        if (toUpdate.Count > 0) await _stock.UpdateRangeAsync(toUpdate, cancellationToken);
        if (toAdd.Count > 0) await _stock.AddRangeAsync(toAdd, cancellationToken);

        var epochChanged = medicine.StockEpoch != ledger.Epoch;
        if (epochChanged)
        {
            medicine.StockEpoch = ledger.Epoch;
            medicine.UpdatedAt = _clock.GetUtcNow();
            await _medicines.UpdateAsync(medicine, cancellationToken);
        }

        return new LedgerSyncResult(ledger, toAdd.Count, toUpdate.Count, toRemove.Count, epochChanged);
    }

    private static StockMovement ToMovement(Guid medicineId, LedgerRow row) => new()
    {
        Id = row.Id,
        MedicineId = medicineId,
        OccurredAt = row.OccurredAt,
        Kind = row.Kind,
        QuantityDelta = row.Delta,
        StockEpoch = row.Epoch,
        Notes = row.Notes,
        Origin = StockMovementOrigin.Derived,
    };

    private static bool SameContent(StockMovement a, StockMovement b)
        => a.Kind == b.Kind
           && a.QuantityDelta == b.QuantityDelta
           && a.OccurredAt == b.OccurredAt
           && a.StockEpoch == b.StockEpoch
           && a.Notes == b.Notes;
}
