using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

public interface IMedicationAdministrationSlotRepository
{
    // Current slots for the medicine, ordered by (Time, Order). Empty
    // list if the medicine uses the legacy dose × frequency model.
    Task<IReadOnlyList<MedicationAdministrationSlot>> ListForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    Task AddRangeAsync(
        IEnumerable<MedicationAdministrationSlot> slots,
        CancellationToken cancellationToken);

    // Deletes every current slot of a medicine; the UpdateMedicine use
    // case calls Delete + AddRange in the same unit of work to
    // atomically replace the list.
    Task DeleteForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);
}
