using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

public interface IMedicationAdministrationSlotRepository
{
    // Current slots for the medicine: those of its most recently
    // recorded slot set, ordered by Order. Empty list if the medicine
    // has no set, or if its latest set is empty (legacy dose x
    // frequency model).
    Task<IReadOnlyList<MedicationAdministrationSlot>> ListForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    // Every recorded set of the medicine with its slots (ordered by
    // Order), in recording order: the history the ledger derivation
    // reads (§4.2).
    Task<IReadOnlyList<AdministrationSlotSetEntry>> ListSetsForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    // Records a new slot set, which becomes the current one. Earlier
    // sets are kept as history (B.1 Phase 2b,
    // docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2). Every slot's
    // SetId must equal set.Id; slots may be empty.
    Task AddSetAsync(
        MedicationAdministrationSlotSet set,
        IReadOnlyList<MedicationAdministrationSlot> slots,
        CancellationToken cancellationToken);
}

public sealed record AdministrationSlotSetEntry(
    MedicationAdministrationSlotSet Set,
    IReadOnlyList<MedicationAdministrationSlot> Slots);
