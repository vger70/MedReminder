using MedReminder.Application.Abstractions;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;

namespace MedReminder.Application.UseCases;

// Updates non-schedule and non-stock fields. Dose / frequency changes
// must go through ChangeMedicationSchedule to preserve the timeline;
// here we only update the "administrative" fields plus, optionally,
// the administration slots.
//
// AdministrationSlots semantics (Increment 10):
//   null           → leave the existing slots alone (the caller is
//                    not touching them in this request).
//   empty list     → clear the slots: the medicine goes back to the
//                    legacy dose × frequency model.
//   non-empty list → atomic replacement (delete + insert) of the
//                    current slots.
// Catalogue linkage semantics (M2, ANALYSIS-DRUG-CATALOGUE.md §2.5):
//   null       → do not touch the existing linkage.
//   non-null   → replace the linkage with the given values. Pass a
//                CatalogueLink with all-null fields to explicitly
//                clear the linkage.
public sealed record UpdateMedicineCommand(
    Guid MedicineId,
    string Name,
    string? ActiveIngredient,
    string? Package,
    string Unit,
    int ThresholdDays,
    NotificationChannels NotificationChannels,
    DateOnly? EndDate,
    string? DoctorName,
    string? Notes,
    bool IsActive,
    IReadOnlyList<AdministrationSlotInput>? AdministrationSlots = null,
    CatalogueLink? Catalogue = null);

public sealed record CatalogueLink(
    string? NationalCode,
    AtcCode? AtcCode,
    Guid? LinkedReferenceMedicineId);

public sealed class UpdateMedicine
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public UpdateMedicine(
        IMedicineRepository medicines,
        IMedicationAdministrationSlotRepository slots,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _slots = slots;
        _uow = uow;
        _clock = clock;
    }

    public async Task ExecuteAsync(UpdateMedicineCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.Name))
            throw new ArgumentException("Medicine name is required.", nameof(cmd));
        if (string.IsNullOrWhiteSpace(cmd.Unit))
            throw new ArgumentException("Unit of measure is required.", nameof(cmd));
        if (cmd.ThresholdDays < 0)
            throw new ArgumentException("Threshold in days cannot be negative.", nameof(cmd));

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        if (cmd.EndDate is { } end && end < medicine.StartDate)
            throw new ArgumentException("Therapy end date cannot precede start date.", nameof(cmd));

        medicine.Name = cmd.Name.Trim();
        medicine.ActiveIngredient = string.IsNullOrWhiteSpace(cmd.ActiveIngredient) ? null : cmd.ActiveIngredient.Trim();
        medicine.Package = string.IsNullOrWhiteSpace(cmd.Package) ? null : cmd.Package.Trim();
        medicine.Unit = cmd.Unit.Trim();
        medicine.ThresholdDays = cmd.ThresholdDays;
        medicine.NotificationChannels = cmd.NotificationChannels;
        medicine.EndDate = cmd.EndDate;
        medicine.DoctorName = string.IsNullOrWhiteSpace(cmd.DoctorName) ? null : cmd.DoctorName.Trim();
        medicine.Notes = string.IsNullOrWhiteSpace(cmd.Notes) ? null : cmd.Notes.Trim();
        medicine.IsActive = cmd.IsActive;
        if (cmd.Catalogue is { } link)
        {
            medicine.NationalCode = string.IsNullOrWhiteSpace(link.NationalCode) ? null : link.NationalCode.Trim();
            medicine.AtcCode = link.AtcCode;
            medicine.LinkedReferenceMedicineId = link.LinkedReferenceMedicineId;
        }
        medicine.UpdatedAt = _clock.GetUtcNow();

        await _medicines.UpdateAsync(medicine, cancellationToken);

        if (cmd.AdministrationSlots is not null)
        {
            await _slots.DeleteForMedicineAsync(medicine.Id, cancellationToken);
            if (cmd.AdministrationSlots.Count > 0)
            {
                await _slots.AddRangeAsync(BuildSlots(medicine.Id, cmd.AdministrationSlots), cancellationToken);
            }
        }

        await _uow.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<MedicationAdministrationSlot> BuildSlots(
        Guid medicineId, IReadOnlyList<AdministrationSlotInput> inputs)
    {
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            yield return new MedicationAdministrationSlot
            {
                MedicineId = medicineId,
                Dose = input.Dose,
                Time = input.Time,
                TimingLabel = string.IsNullOrWhiteSpace(input.TimingLabel) ? null : input.TimingLabel.Trim(),
                Order = i,
            };
        }
    }
}
