using MedReminder.Application.Abstractions;
using MedReminder.Application.Ledger;
using MedReminder.Application.Migrations;

namespace MedReminder.Application.Monitoring;

// Keeps every medicine's derived ledger rows up to date (B.1 Phase
// 2c-2, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.3): automatic
// consumption through yesterday, intake consumption, count corrections,
// all produced by LedgerDeriver from the facts and written by
// LedgerSynchronizer. A thin wrapper, as §4.3 plans.
//
// Runs on every medicine, active or not: the derivation skips the days
// on which a medicine was inactive (activity history, D15), and a
// medicine left out would keep rows that depend on when it was last
// derived.
//
// Idempotency: derived rows have deterministic ids, so re-running
// writes nothing when no fact changed. Against concurrent calls
// (hosted-service tick and "Check now") RunAsync holds WriteGate.
public sealed class ConsumptionCatchUp
{
    private readonly IMedicineRepository _medicines;
    private readonly LedgerSynchronizer _ledger;
    private readonly IUnitOfWork _uow;
    private readonly AsNeededSlotBackfill? _asNeededBackfill;
    private readonly SlotPresetBackfill? _presetBackfill;

    public ConsumptionCatchUp(
        IMedicineRepository medicines,
        LedgerSynchronizer ledger,
        IUnitOfWork uow,
        AsNeededSlotBackfill? asNeededBackfill = null,
        SlotPresetBackfill? presetBackfill = null)
    {
        _medicines = medicines;
        _ledger = ledger;
        _uow = uow;
        _asNeededBackfill = asNeededBackfill;
        _presetBackfill = presetBackfill;
    }

    // Returns the number of derived rows created.
    public Task<int> RunAsync(CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(RunCoreAsync, cancellationToken);

    private async Task<int> RunCoreAsync(CancellationToken cancellationToken)
    {
        // One-time data migrations, before the first derivation that
        // would book the day with the old as-needed data. The preset link
        // runs after it, so the slots it flagged get their preset too.
        if (_asNeededBackfill is not null) await _asNeededBackfill.RunAsync(cancellationToken);
        if (_presetBackfill is not null) await _presetBackfill.RunAsync(cancellationToken);

        var medicines = await _medicines.ListAllAsync(cancellationToken);

        var created = 0;
        var changed = false;
        foreach (var medicine in medicines)
        {
            var result = await _ledger.SynchronizeAsync(medicine, cancellationToken);
            created += result.RowsAdded;
            changed |= result.RowsAdded + result.RowsChanged + result.RowsRemoved > 0 || result.EpochChanged;
        }

        if (changed)
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        return created;
    }

    internal DateOnly LocalToday() => _ledger.LocalToday();
}
