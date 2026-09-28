using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.UseCases;

public sealed record DeleteMedicineCommand(Guid MedicineId);

public enum DeleteMedicineOutcome
{
    Deleted = 0,
    NotFound = 1,
    // Stock entries, intakes, counts or suspensions were recorded: the
    // medicine is deactivated instead, or its facts retracted first.
    HasRecordedFacts = 2,
}

// Deletes a medicine entered by mistake. Allowed only while no fact was
// recorded for it: no stock entry (User or Legacy), intake, stock count
// or suspension. Schedule rows, slot sets, activity changes and derived
// stock rows are configuration or computed from it, and go with the
// medicine. A medicine with history is deactivated instead, so the
// history is never lost by accident.
//
// With sync enabled the deletion is a MedicineDeleted operation, which
// other devices apply whatever they recorded meanwhile.
public sealed class DeleteMedicine
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IStockCountRepository _counts;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicineDeletionRepository _deletion;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public DeleteMedicine(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationIntakeRepository intakes,
        IStockCountRepository counts,
        IMedicationSuspensionRepository suspensions,
        IMedicineDeletionRepository deletion,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _stock = stock;
        _intakes = intakes;
        _counts = counts;
        _suspensions = suspensions;
        _deletion = deletion;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    // Read-only check for the UI, before asking for confirmation.
    public async Task<DeleteMedicineOutcome> CheckAsync(Guid medicineId, CancellationToken cancellationToken)
    {
        if (await _medicines.GetAsync(medicineId, cancellationToken) is null) return DeleteMedicineOutcome.NotFound;
        return await HasRecordedFactsAsync(medicineId, cancellationToken)
            ? DeleteMedicineOutcome.HasRecordedFacts
            : DeleteMedicineOutcome.Deleted;
    }

    public Task<DeleteMedicineOutcome> ExecuteAsync(DeleteMedicineCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return WriteGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    private async Task<DeleteMedicineOutcome> ExecuteCoreAsync(DeleteMedicineCommand cmd, CancellationToken cancellationToken)
    {
        if (await _medicines.GetAsync(cmd.MedicineId, cancellationToken) is null) return DeleteMedicineOutcome.NotFound;
        if (await HasRecordedFactsAsync(cmd.MedicineId, cancellationToken)) return DeleteMedicineOutcome.HasRecordedFacts;

        await _deletion.RemoveAsync(cmd.MedicineId, cancellationToken);
        await _operations.AppendAsync([Operations.Deleted(cmd.MedicineId, _clock.GetUtcNow())], cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return DeleteMedicineOutcome.Deleted;
    }

    private async Task<bool> HasRecordedFactsAsync(Guid medicineId, CancellationToken cancellationToken)
        => (await _stock.ListForMedicineAsync(medicineId, cancellationToken)).Any(m => m.Origin != StockMovementOrigin.Derived)
            || (await _intakes.ListForMedicineAsync(medicineId, cancellationToken)).Count > 0
            || (await _counts.ListForMedicineAsync(medicineId, cancellationToken)).Count > 0
            || (await _suspensions.ListForMedicineAsync(medicineId, cancellationToken)).Count > 0;
}
