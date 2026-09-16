using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

public interface IMedicationAdministrationSlotRepository
{
    // Slot correnti della medicina, ordinati per (Time, Order).
    // Lista vuota se la medicina usa il modello legacy dose×frequenza.
    Task<IReadOnlyList<MedicationAdministrationSlot>> ListForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    Task AddRangeAsync(
        IEnumerable<MedicationAdministrationSlot> slots,
        CancellationToken cancellationToken);

    // Cancella tutti gli slot correnti di una medicina; l'UpdateMedicine
    // use case chiama Delete + AddRange nella stessa unità di lavoro per
    // sostituire l'elenco atomically.
    Task DeleteForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);
}
