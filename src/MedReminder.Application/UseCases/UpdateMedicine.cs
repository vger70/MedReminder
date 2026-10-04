using MedReminder.Application.Abstractions;
using MedReminder.Application.Ledger;
using MedReminder.Application.Sync;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.UseCases;

// Updates non-schedule and non-stock fields. Dose / frequency changes
// must go through ChangeMedicationSchedule to preserve the timeline;
// here we only update the "administrative" fields plus, optionally,
// the administration slots and the therapy start date.
//
// StartDate: null leaves it alone. A new date records the facts that
// make the schedule and the slots follow it (TherapyStartChange); the
// ledger is derived again from the new date.
//
// AdministrationSlots semantics (Increment 10):
//   null           → leave the existing slots alone (the caller is
//                    not touching them in this request).
//   empty list     → clear the slots: the medicine goes back to the
//                    legacy dose × frequency model.
//   non-empty list → replace the current slots.
// Both non-null cases record a new slot set, effective from today, that
// becomes the current one; earlier sets stay as history (B.1 Phase 2b,
// ANALYSIS-B1-MOBILE-SYNC.md §4.2). Without a Baseline the set is
// recorded even when the slots did not change, as the former delete +
// insert did (the new slot ids are the dose-reminder dedup keys); with a
// Baseline it is recorded only when the list differs from the
// baseline's.
// Catalogue linkage semantics (M2, ANALYSIS-DRUG-CATALOGUE.md §2.5):
//   null       → do not touch the existing linkage.
//   non-null   → replace the linkage with the given values. Pass a
//                CatalogueLink with all-null fields to explicitly
//                clear the linkage.
//
// Baseline (B.1 Phase 3a, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §7.4, stale forms): the values the edit form loaded, expressed as the
// command that would save them unchanged. When set, a field is written
// only when the command's value differs from the baseline's, so a save
// never overwrites a value the user did not touch (with sync, a value
// another device changed after the form opened). The slots and the
// catalogue linkage follow the same rule. Without a baseline every
// field is written, as before. Operations are emitted only for fields
// whose stored value changes.
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
    CatalogueLink? Catalogue = null,
    DateOnly? StartDate = null)
{
    public UpdateMedicineCommand? Baseline { get; init; }
}

public sealed record CatalogueLink(
    string? NationalCode,
    AtcCode? AtcCode,
    Guid? LinkedReferenceMedicineId);

public sealed class UpdateMedicine
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IMedicineActivityRepository _activity;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public UpdateMedicine(
        IMedicineRepository medicines,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationAdministrationSlotRepository slots,
        IMedicineActivityRepository activity,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _schedules = schedules;
        _slots = slots;
        _activity = activity;
        _operations = operations;
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
        await WriteGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    private async Task ExecuteCoreAsync(UpdateMedicineCommand cmd, CancellationToken cancellationToken)
    {
        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        var baseline = cmd.Baseline;
        bool Touched<T>(Func<UpdateMedicineCommand, T> value)
            => baseline is null || !EqualityComparer<T>.Default.Equals(value(cmd), value(baseline));

        var previousStart = medicine.StartDate;
        var startDate = cmd.StartDate is { } requested && Touched(c => c.StartDate) ? requested : previousStart;
        var endDate = Touched(c => c.EndDate) ? cmd.EndDate : medicine.EndDate;
        if (endDate is { } end && end < startDate)
            throw new ArgumentException("Therapy end date cannot precede start date.", nameof(cmd));

        var before = MedicineFieldCodec.Snapshot(medicine);

        if (Touched(c => Text(c.Name))) medicine.Name = cmd.Name.Trim();
        if (Touched(c => Text(c.ActiveIngredient))) medicine.ActiveIngredient = Text(cmd.ActiveIngredient);
        if (Touched(c => Text(c.Package))) medicine.Package = Text(cmd.Package);
        if (Touched(c => Text(c.Unit))) medicine.Unit = cmd.Unit.Trim();
        if (Touched(c => c.ThresholdDays)) medicine.ThresholdDays = cmd.ThresholdDays;
        if (Touched(c => c.NotificationChannels)) medicine.NotificationChannels = cmd.NotificationChannels;
        if (Touched(c => c.EndDate)) medicine.EndDate = cmd.EndDate;
        medicine.StartDate = startDate;
        if (Touched(c => Text(c.DoctorName))) medicine.DoctorName = Text(cmd.DoctorName);
        if (Touched(c => Text(c.Notes))) medicine.Notes = Text(cmd.Notes);

        var now = _clock.GetUtcNow();
        MedicineActivityChange? activityChange = null;
        if (Touched(c => c.IsActive))
        {
            activityChange = await MedicineActivity.RecordAsync(
                _activity, medicine, cmd.IsActive, now, _clock.LocalTimeZone, cancellationToken);
            medicine.IsActive = cmd.IsActive;
        }
        if (Touched(c => c.RemindOnDose)) medicine.RemindOnDose = cmd.RemindOnDose;
        if (cmd.Catalogue is { } link)
        {
            if (Touched(c => Text(c.Catalogue?.NationalCode))) medicine.NationalCode = Text(link.NationalCode);
            if (Touched(c => c.Catalogue?.AtcCode)) medicine.AtcCode = link.AtcCode;
            if (Touched(c => c.Catalogue?.LinkedReferenceMedicineId))
                medicine.LinkedReferenceMedicineId = link.LinkedReferenceMedicineId;
        }
        medicine.UpdatedAt = now;

        await _medicines.UpdateAsync(medicine, cancellationToken);

        var operations = new List<SyncOperationBody>(Operations.FieldChanges(before, medicine));
        if (activityChange is not null) operations.Add(Operations.Activity(activityChange));
        // Before a new slot set is added: the slot facts follow the sets
        // recorded so far.
        if (startDate != previousStart)
        {
            operations.AddRange(await TherapyStartChange.RecordAsync(
                medicine, previousStart, now, _schedules, _slots, cancellationToken));
        }

        if (cmd.AdministrationSlots is { } slotInputs
            && (baseline?.AdministrationSlots is not { } baseSlots || !slotInputs.SequenceEqual(baseSlots)))
        {
            var set = new MedicationAdministrationSlotSet
            {
                MedicineId = medicine.Id,
                EffectiveFrom = DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(now, _clock.LocalTimeZone).DateTime),
                RecordedAt = now,
            };
            var built = AdministrationSlotSetBuilder.BuildSlots(set, slotInputs);
            await _slots.AddSetAsync(set, built, cancellationToken);
            operations.Add(Operations.SlotSet(set, built));
        }

        await _operations.AppendAsync(operations, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    private static string? Text(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
