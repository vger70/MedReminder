using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Domain.Sync;

// Operation catalogue, schema versions 1 to 5 (B.1 Phase 3a,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2, §7.2). One operation
// per user fact or per changed register, never a derived row:
// consumption, count corrections, StockEpoch and the schedule summary on
// Medicine are recomputed by every device (§3.4).
//
// Every operation carries the medicine it touches, so the apply step
// re-derives only the medicines an incoming segment changed. A
// profile-level operation (ProfileSettingChanged) carries Guid.Empty:
// it touches no medicine. The ids
// are those of the local rows, so replaying an operation is idempotent.
// Wire names and field names are part of the sync format: renaming a
// type, a property or an enum member is a format change.
//
// BaseVersion (Phase 3b): on the three operations whose register can
// raise a conflict (a medicine field, the schedule row of a date, the
// slot set), the version of that register the writing device held when
// it wrote, null when it held none. The writer had seen every
// version up to its base, so two writes are concurrent exactly when the
// later one's base is older than the earlier one (RegisterMerge). Set by
// the operation log, not by the use cases.
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
    string? Value,
    HybridTimestamp? BaseVersion = null) : SyncOperationBody(MedicineId);

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
    DateTimeOffset RecordedAt,
    HybridTimestamp? BaseVersion = null) : SyncOperationBody(MedicineId);

// A whole slot set; an empty list clears the slots from EffectiveFrom.
public sealed record SlotSetRecorded(
    Guid MedicineId,
    Guid SetId,
    DateOnly EffectiveFrom,
    DateTimeOffset RecordedAt,
    IReadOnlyList<SlotValue> Slots,
    HybridTimestamp? BaseVersion = null) : SyncOperationBody(MedicineId);

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

// Tombstone of a deleted medicine (operation schema version 2). The
// medicine and every row that refers to it are removed; it wins over any
// operation for the medicine whatever the order of arrival, including
// facts recorded concurrently on another device. The local use case
// deletes only a medicine without recorded facts.
public sealed record MedicineDeleted(
    Guid MedicineId,
    DateTimeOffset RecordedAt) : SyncOperationBody(MedicineId);

// A low-stock email sent for a stock epoch of the medicine (operation
// schema version 4): the other devices of the group do not send it again
// for that epoch. A fact, never retracted. Stage (schema version 6) is
// the warning stage; a payload without it is the first stage.
public sealed record EmailNotificationSent(
    Guid MedicineId,
    Guid NotificationId,
    int StockEpoch,
    Guid? EpochFactId,
    DateTimeOffset SentAt,
    int Stage = 1) : SyncOperationBody(MedicineId);

// Household step H3c (operation schema version 5; docs/analysis/
// ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §11): the household that adopted
// this profile group. A device of the group that is not in that household
// is asked to join it instead of publishing its own. When two households
// claim the group, the earliest claim by HLC wins. Touches no medicine;
// the operation log is the state (it is part of every image).
public sealed record HouseholdLinked(
    Guid HouseholdId,
    DateTimeOffset LinkedAt) : SyncOperationBody(Guid.Empty);

// A prescription as a whole (operation schema version 7; docs/notes/
// EVOLUTION-PROPOSALS-2.md §3.2): written when it is recorded, changed or
// deleted. Last writer wins per prescription, without a conflict entry;
// Deleted removes it, unless a later write brings it back.
public sealed record PrescriptionChanged(
    Guid MedicineId,
    Guid PrescriptionId,
    DateOnly? RequestedOn,
    DateOnly? IssuedOn,
    string? Code,
    int? Packages,
    DateOnly? ValidUntil,
    DateOnly? CollectedOn,
    bool Deleted,
    DateTimeOffset RecordedAt) : SyncOperationBody(MedicineId);

// A replicated setting of the profile (operation schema version 3,
// closing P8 of docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §2, §4.2):
// its display name and the notification recipients. Last writer wins
// per setting, without a conflict entry. MedicineId is Guid.Empty.
public sealed record ProfileSettingChanged(
    string Setting,
    string? Value) : SyncOperationBody(Guid.Empty);

// Names of the replicated profile settings (ProfileSettingChanged.Setting).
// Part of the sync format.
public static class ProfileSetting
{
    public const string DisplayName = "DisplayName";
    public const string ToAddress = "ToAddress";
    public const string CaregiverAddress = "CaregiverAddress";
    public const string DoctorAddress = "DoctorAddress";

    public static readonly IReadOnlyList<string> All = [DisplayName, ToAddress, CaregiverAddress, DoctorAddress];
}
