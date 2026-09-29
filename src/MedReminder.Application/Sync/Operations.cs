using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

// Builds the catalogue operation for a fact row a use case has just
// written (B.1 Phase 3a). One place per row type, so every use case
// writing the same kind of fact emits the same operation.
internal static class Operations
{
    // SyncOperation.EntityId of an operation.
    public static Guid EntityOf(SyncOperationBody body) => body switch
    {
        MedicineActivityChanged a => a.ChangeId,
        ScheduleRowRecorded s => s.RowId,
        SlotSetRecorded s => s.SetId,
        StockEntryRecorded e => e.MovementId,
        IntakeRecorded i => i.IntakeId,
        StockCountRecorded c => c.CountId,
        SuspensionRecorded s => s.SuspensionId,
        SuspensionEndChanged s => s.SuspensionId,
        FactRetracted r => r.RetractionId,
        EmailNotificationSent e => e.NotificationId,
        _ => body.MedicineId,
    };

    public static MedicineCreated Created(Medicine medicine)
        => new(medicine.Id, medicine.StartDate, medicine.CreatedAt, MedicineFieldCodec.Snapshot(medicine));

    // One MedicineFieldChanged per field whose value differs from
    // `before` (a MedicineFieldCodec.Snapshot taken before the edit).
    public static IEnumerable<SyncOperationBody> FieldChanges(
        IReadOnlyList<MedicineFieldValue> before, Medicine after)
        => MedicineFieldCodec.Diff(before, after)
            .Select(v => new MedicineFieldChanged(after.Id, v.Field, v.Value));

    public static MedicineActivityChanged Activity(MedicineActivityChange change)
        => new(change.MedicineId, change.Id, change.Day, change.Active, change.RecordedAt);

    public static ScheduleRowRecorded ScheduleRow(MedicationScheduleHistory row)
        => new(row.MedicineId, row.Id, row.EffectiveFrom, row.DosePerAdministration,
            row.AdministrationsPerDay, row.ScheduleKind, row.SchedulePayload, row.RecordedAt);

    public static SlotSetRecorded SlotSet(
        MedicationAdministrationSlotSet set, IReadOnlyList<MedicationAdministrationSlot> slots)
        => new(set.MedicineId, set.Id, set.EffectiveFrom, set.RecordedAt,
            [.. slots.Select(s => new SlotValue(s.Id, s.Dose, s.Time, s.TimingLabel, s.Order))]);

    public static StockEntryRecorded StockEntry(StockMovement movement)
        => new(movement.MedicineId, movement.Id, movement.Kind, movement.QuantityDelta,
            movement.OccurredAt, movement.Notes);

    public static IntakeRecorded Intake(MedicationIntake intake)
        => new(intake.MedicineId, intake.Id, intake.Day, intake.Status, intake.Quantity,
            intake.ScheduledAt, intake.ActualAt, intake.Notes, intake.RecordedAt);

    public static StockCountRecorded Count(StockCount count)
        => new(count.MedicineId, count.Id, count.CountDay, count.CountedQuantity, count.TakenToday,
            count.ThresholdAtCount, count.RecordedAt, count.Notes, count.LedgerAtStartOfDay,
            count.CountDayScheduled, count.Correction, count.MaterializesCountDay, count.AdvancesEpoch);

    public static SuspensionRecorded Suspension(MedicationSuspension suspension)
        => new(suspension.MedicineId, suspension.Id, suspension.StartDate, suspension.EndDate,
            suspension.Reason, suspension.RecordedAt);

    public static SuspensionEndChanged SuspensionEnd(MedicationSuspension suspension)
        => new(suspension.MedicineId, suspension.Id, suspension.EndDate);

    public static MedicineDeleted Deleted(Guid medicineId, DateTimeOffset recordedAt)
        => new(medicineId, recordedAt);

    public static EmailNotificationSent EmailSent(SentEmailNotification sent)
        => new(sent.MedicineId, sent.Id, sent.StockEpoch, sent.EpochFactId, sent.SentAt);

    public static FactRetracted Retraction(FactRetraction retraction)
        => new(retraction.MedicineId, retraction.Id, retraction.Kind, retraction.FactId, retraction.RecordedAt);
}
