using MedReminder.Application.Abstractions;
using MedReminder.Application.Monitoring;
using MedReminder.Domain.Ledger;

namespace MedReminder.Application.Ledger;

public sealed record RetractFactCommand(Guid MedicineId, FactKind Kind, Guid FactId);

// Retracts a mistaken fact (B.1 Phase 2d, D8,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2): the fact row is
// removed, a tombstone is recorded, and the medicine's ledger is derived
// again without it (consumption, corrections and StockEpoch follow).
// Allowed only when FactHistoryQuery says so: not Legacy, and no stock
// count recorded after the fact.
public sealed class RetractFact
{
    private readonly FactHistoryQuery _history;
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IStockCountRepository _counts;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IFactRetractionRepository _retractions;
    private readonly LedgerSynchronizer _ledger;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public RetractFact(
        FactHistoryQuery history,
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationIntakeRepository intakes,
        IStockCountRepository counts,
        IMedicationSuspensionRepository suspensions,
        IFactRetractionRepository retractions,
        LedgerSynchronizer ledger,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _history = history;
        _medicines = medicines;
        _stock = stock;
        _intakes = intakes;
        _counts = counts;
        _suspensions = suspensions;
        _retractions = retractions;
        _ledger = ledger;
        _uow = uow;
        _clock = clock;
    }

    // Runs under MonitoringGate, like every ledger writer. Returns the
    // tombstone id.
    public Task<Guid> ExecuteAsync(RetractFactCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return MonitoringGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    private async Task<Guid> ExecuteCoreAsync(RetractFactCommand cmd, CancellationToken cancellationToken)
    {
        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        var item = (await _history.LoadAsync(medicine.Id, cancellationToken))
            .FirstOrDefault(i => i.Kind == cmd.Kind && i.FactId == cmd.FactId)
            ?? throw new InvalidOperationException($"Fact {cmd.FactId} not found.");
        if (!item.CanRetract)
        {
            throw new InvalidOperationException($"Fact {cmd.FactId} cannot be retracted ({item.Block}).");
        }

        var facts = await _ledger.LoadFactsAsync(medicine, cancellationToken);
        switch (cmd.Kind)
        {
            case FactKind.StockEntry:
                var entry = facts.UserEntries.Single(e => e.Id == cmd.FactId);
                await _stock.RemoveRangeAsync([entry], cancellationToken);
                facts = facts with { UserEntries = facts.UserEntries.Where(e => e.Id != cmd.FactId).ToList() };
                break;
            case FactKind.Intake:
                var intake = (await _intakes.ListForMedicineAsync(medicine.Id, cancellationToken))
                    .Single(i => i.Id == cmd.FactId);
                await _intakes.RemoveAsync(intake, cancellationToken);
                facts = facts with { Intakes = facts.Intakes.Where(i => i.Id != cmd.FactId).ToList() };
                break;
            case FactKind.StockCount:
                var count = (await _counts.ListForMedicineAsync(medicine.Id, cancellationToken))
                    .Single(c => c.Id == cmd.FactId);
                await _counts.RemoveAsync(count, cancellationToken);
                facts = facts with { Counts = facts.Counts.Where(c => c.Id != cmd.FactId).ToList() };
                break;
            case FactKind.Suspension:
                var suspension = (await _suspensions.ListForMedicineAsync(medicine.Id, cancellationToken))
                    .Single(s => s.Id == cmd.FactId);
                await _suspensions.RemoveAsync(suspension, cancellationToken);
                facts = facts with { Suspensions = facts.Suspensions.Where(s => s.Id != cmd.FactId).ToList() };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cmd), cmd.Kind, "Unknown fact kind.");
        }

        var now = _clock.GetUtcNow();
        var tombstone = new FactRetraction
        {
            MedicineId = medicine.Id,
            FactId = cmd.FactId,
            Kind = cmd.Kind,
            RecordedAt = now,
        };
        await _retractions.AddAsync(tombstone, cancellationToken);

        await _ledger.ApplyAsync(medicine, facts, cancellationToken);
        medicine.UpdatedAt = now;
        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return tombstone.Id;
    }
}
