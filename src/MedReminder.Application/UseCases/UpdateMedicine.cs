using MedReminder.Application.Abstractions;
using MedReminder.Application.Ledger;
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
//   non-empty list → replace the current slots.
// Both non-null cases record a new slot set, effective from today, that
// becomes the current one; earlier sets stay as history (B.1 Phase 2b,
// ANALYSIS-B1-MOBILE-SYNC.md §4.2). The set is recorded even when the
// slots did not change, as the former delete + insert did: the new slot
// ids are the dose-reminder dedup keys.
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
    bool RemindOnDose = false,
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
    private readonly IMedicineActivityRepository _activity;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public UpdateMedicine(
        IMedicineRepository medicines,
        IMedicationAdministrationSlotRepository slots,
        IMedicineActivityRepository activity,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _slots = slots;
        _activity = activity;
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
        await MedicineActivity.RecordAsync(
            _activity, medicine, cmd.IsActive, _clock.GetUtcNow(), _clock.LocalTimeZone, cancellationToken);
        medicine.IsActive = cmd.IsActive;
        medicine.RemindOnDose = cmd.RemindOnDose;
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
            var now = medicine.UpdatedAt;
            var set = new MedicationAdministrationSlotSet
            {
                MedicineId = medicine.Id,
                EffectiveFrom = DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(now, _clock.LocalTimeZone).DateTime),
                RecordedAt = now,
            };
            await _slots.AddSetAsync(
                set, AdministrationSlotSetBuilder.BuildSlots(set, cmd.AdministrationSlots), cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);
    }
}
