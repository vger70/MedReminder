using MedReminder.Domain.Medicines;

namespace MedReminder.Application.UseCases;

// DTO used by commands (AddMedicine, UpdateMedicine) to carry the
// slots from the UI to the Application without exposing the domain
// entity. Order is derived from the list index when the command is
// executed — the UI does not have to compute it manually.
public sealed record AdministrationSlotInput(
    decimal Dose,
    TimeOnly? Time,
    string? TimingLabel,
    bool IsAsNeeded = false);

// Builds the slot rows of a new slot set from the command inputs
// (shared by AddMedicine and UpdateMedicine).
internal static class AdministrationSlotSetBuilder
{
    public static IReadOnlyList<MedicationAdministrationSlot> BuildSlots(
        MedicationAdministrationSlotSet set, IReadOnlyList<AdministrationSlotInput> inputs)
    {
        var slots = new List<MedicationAdministrationSlot>(inputs.Count);
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            slots.Add(new MedicationAdministrationSlot
            {
                MedicineId = set.MedicineId,
                SetId = set.Id,
                Dose = input.Dose,
                Time = input.Time,
                TimingLabel = string.IsNullOrWhiteSpace(input.TimingLabel) ? null : input.TimingLabel.Trim(),
                Order = i,
                IsAsNeeded = input.IsAsNeeded,
            });
        }
        return slots;
    }
}
