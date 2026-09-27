using MedReminder.Domain.Ledger;

namespace MedReminder.Application.Abstractions;

// Tombstones of retracted facts (B.1 Phase 2d, D8).
public interface IFactRetractionRepository
{
    Task<IReadOnlyList<FactRetraction>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken);

    Task AddAsync(FactRetraction retraction, CancellationToken cancellationToken);
}
