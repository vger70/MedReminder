using MedReminder.Application.Abstractions;
using MedReminder.Application.Ledger;

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
// (hosted-service tick and "Check now") RunAsync holds MonitoringGate.
public sealed class ConsumptionCatchUp
{
    private readonly IMedicineRepository _medicines;
    private readonly LedgerSynchronizer _ledger;
    private readonly IUnitOfWork _uow;

    public ConsumptionCatchUp(
        IMedicineRepository medicines,
        LedgerSynchronizer ledger,
        IUnitOfWork uow)
    {
        _medicines = medicines;
        _ledger = ledger;
        _uow = uow;
    }

    // Returns the number of derived rows created.
    public Task<int> RunAsync(CancellationToken cancellationToken)
        => MonitoringGate.RunExclusiveAsync(RunCoreAsync, cancellationToken);

    private async Task<int> RunCoreAsync(CancellationToken cancellationToken)
    {
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
