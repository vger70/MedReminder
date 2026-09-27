using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Domain.Sync;

// Operation catalogue, schema version 1 (B.1 Phase 3a,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2, §7.2). One operation
// per user fact or per changed register, never a derived row:
// consumption, count corrections, StockEpoch and the schedule summary on
// Medicine are recomputed by every device (§3.4).
//
// Every operation carries the medicine it touches, so the apply step
// re-derives only the medicines an incoming segment changed. The ids
// are those of the local rows, so replaying an operation is idempotent.
// Wire names and field names are part of the sync format: renaming a
// type, a property or an enum member is a format change.
public abstract record SyncOperationBody(Guid MedicineId);

// A new medicine with the value of every replicated field
// (MedicineFieldCodec, in declaration order). IsActive starts true; the
// schedule, slots and initial stock follow as their own operations.
public sealed record MedicineCreated(
    Guid MedicineId,
    DateOnly StartDate,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MedicineFieldValue> Fields) : SyncOperationBody(MedicineId);

public sealed record MedicineFieldValue(string Field, string? Value);

// One replicated scalar field of a medicine, last writer wins per field.
public sealed record MedicineFieldChanged(
    Guid MedicineId,
    string Field,
    string? Value) : SyncOperationBody(MedicineId);

// A dated activation / deactivation (activity history, D15).
public sealed record MedicineActivityChanged(
    Guid MedicineId,
    Guid ChangeId,
    DateOnly Day,
    bool Active,
    DateTimeOffset RecordedAt) : SyncOperationBody(MedicineId);

public sealed record ScheduleRowRecorded(
    Guid MedicineId,
    Guid RowId,
    DateOnly EffectiveFrom,
    decimal DosePerAdministration,
    int AdministrationsPerDay,
    ScheduleKind ScheduleKind,
    string? SchedulePayload,
    DateTimeOffset RecordedAt) : SyncOperationBody(MedicineId);

// A whole slot set; an empty list clears the slots from EffectiveFrom.
public sealed record SlotSetRecorded(
    Guid MedicineId,
    Guid SetId,
    DateOnly EffectiveFrom,
    DateTimeOffset RecordedAt,
    IReadOnlyList<SlotValue> Slots) : SyncOperationBody(MedicineId);

public sealed record SlotValue(Guid SlotId, decimal Dose, TimeOnly? Time, string? TimingLabel, int Order);

// A user stock entry: InitialLoad, NewPackage, ManualAdd,
// PositiveCorrection (AddStock) or NegativeCorrection (AdjustStockDown).
public sealed record StockEntryRecorded(
    Guid MedicineId,
    Guid MovementId,
    StockMovementKind Kind,
    decimal QuantityDelta,
    DateTimeOffset OccurredAt,
    string? Notes) : SyncOperationBody(MedicineId);

public sealed record IntakeRecorded(
    Guid MedicineId,
    Guid IntakeId,
    DateOnly Day,
    IntakeStatus Status,
    decimal Quantity,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? ActualAt,
    string? Notes,
    DateTimeOffset RecordedAt) : SyncOperationBody(MedicineId);

// The count inputs and the outcome evaluated on the recording device.
// Phase 3b decides how the outcome is re-evaluated when facts from other
// devices arrive (§4.3 rule 3).
public sealed record StockCountRecorded(
    Guid MedicineId,
    Guid CountId,
    DateOnly CountDay,
    decimal CountedQuantity,
    decimal TakenToday,
    int ThresholdAtCount,
    DateTimeOffset RecordedAt,
    string? Notes,
    decimal LedgerAtStartOfDay,
    decimal CountDayScheduled,
    decimal Correction,
    bool MaterializesCountDay,
    bool AdvancesEpoch) : SyncOperationBody(MedicineId);

public sealed record SuspensionRecorded(
    Guid MedicineId,
    Guid SuspensionId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Reason,
    DateTimeOffset RecordedAt) : SyncOperationBody(MedicineId);

// EndDate of a suspension, last writer wins (ResumeMedication).
public sealed record SuspensionEndChanged(
    Guid MedicineId,
    Guid SuspensionId,
    DateOnly? EndDate) : SyncOperationBody(MedicineId);

// Tombstone of a retracted fact (D8); wins over the fact whatever the
// order of arrival.
public sealed record FactRetracted(
    Guid MedicineId,
    Guid RetractionId,
    FactKind Kind,
    Guid FactId,
    DateTimeOffset RecordedAt) : SyncOperationBody(MedicineId);
