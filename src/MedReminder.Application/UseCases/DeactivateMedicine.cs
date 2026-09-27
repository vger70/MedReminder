using MedReminder.Application.Abstractions;

namespace MedReminder.Application.UseCases;

// Marks a medicine as inactive (the "Deactivate" action of the main
// window). Moved out of MainForm in B.1 Phase 2a so that every data
// write goes through an Application use case (ANALYSIS-B1-MOBILE-SYNC.md
// §2 P8, §7.2). Only IsActive and UpdatedAt change; the other fields
// are not normalised or re-validated, unlike UpdateMedicine, so the
// action behaves exactly as the former inline write did.
//
// Returns false when the medicine no longer exists: the caller treats
// it as a no-op, as the UI did before.
public sealed record DeactivateMedicineCommand(Guid MedicineId);

public sealed class DeactivateMedicine
{
    private readonly IMedicineRepository _medicines;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public DeactivateMedicine(
        IMedicineRepository medicines,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _uow = uow;
        _clock = clock;
    }

    public async Task<bool> ExecuteAsync(DeactivateMedicineCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken);
        if (medicine is null) return false;

        medicine.IsActive = false;
        medicine.UpdatedAt = _clock.GetUtcNow();

        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return true;
    }
}
