using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Prototypes.Sync;

// Replicated operations (ANALYSIS-B1-MOBILE-SYNC.md §3.3, §4.2). Each
// operation is a user fact or a field assignment; derived rows are
// never operations. OpId makes application idempotent; Hlc orders
// last-writer-wins decisions and snapshot evaluation of count anchors.
public abstract record Operation(Guid OpId, Hlc Hlc, Guid MedicineId);

public enum MedicineField
{
    Name,
    ThresholdDays,
    EndDate,
    Notes,
}

public sealed record SlotValue(decimal Dose, TimeOnly? Time);

// Creation carries only immutable data. Mutable fields, the first
// schedule row, the initial load and the first slot set are separate
// operations emitted in the same local transaction.
public sealed record MedicineCreated(Guid OpId, Hlc Hlc, Guid MedicineId, DateOnly StartDate, string Unit)
    : Operation(OpId, Hlc, MedicineId);

public sealed record MedicineFieldSet(Guid OpId, Hlc Hlc, Guid MedicineId, MedicineField Field, string? Value)
    : Operation(OpId, Hlc, MedicineId);

// Activity history (§4.2, D15): the medicine is active on day d when
// the latest event (by HLC) whose Day <= d says so; no event = active.
public sealed record ActivitySet(Guid OpId, Hlc Hlc, Guid MedicineId, bool Active, DateOnly Day)
    : Operation(OpId, Hlc, MedicineId);

// Slot set effective from a day (finding of S9: slots need a history,
// see the spike results). A set recorded on day T has EffectiveFrom = T;
// the set given at creation has DateOnly.MinValue, because today's slots
// apply to every day not yet booked, before StartDate included.
public sealed record SlotsSet(Guid OpId, Hlc Hlc, Guid MedicineId, DateOnly EffectiveFrom, IReadOnlyList<SlotValue> Slots)
    : Operation(OpId, Hlc, MedicineId);

// Keyed by (MedicineId, EffectiveFrom): higher HLC wins.
public sealed record ScheduleRowAdded(Guid OpId, Hlc Hlc, Guid MedicineId, DateOnly EffectiveFrom, decimal Dose, int AdministrationsPerDay)
    : Operation(OpId, Hlc, MedicineId);

public sealed record SuspensionCreated(Guid OpId, Hlc Hlc, Guid MedicineId, Guid SuspensionId, DateOnly StartDate)
    : Operation(OpId, Hlc, MedicineId);

public sealed record SuspensionEndSet(Guid OpId, Hlc Hlc, Guid MedicineId, Guid SuspensionId, DateOnly? EndDate)
    : Operation(OpId, Hlc, MedicineId);

// AddStock (positive kinds) and AdjustStockDown. Delta is signed.
public sealed record StockEntryAdded(Guid OpId, Hlc Hlc, Guid MedicineId, Guid FactId, StockMovementKind Kind, decimal Delta, DateTimeOffset OccurredAt)
    : Operation(OpId, Hlc, MedicineId);

// Count anchor (§3.4). ThresholdAtCount is captured so the epoch rule of
// ReconcileStock can be evaluated without a threshold history.
public sealed record StockCountRecorded(Guid OpId, Hlc Hlc, Guid MedicineId, Guid FactId, DateOnly CountDay, decimal Counted, decimal TakenToday, int ThresholdAtCount)
    : Operation(OpId, Hlc, MedicineId);

public sealed record IntakeRecorded(Guid OpId, Hlc Hlc, Guid MedicineId, Guid IntakeId, DateOnly Day, IntakeStatus Status, decimal Quantity)
    : Operation(OpId, Hlc, MedicineId);

public sealed record IntakeStatusSet(Guid OpId, Hlc Hlc, Guid MedicineId, Guid IntakeId, IntakeStatus Status)
    : Operation(OpId, Hlc, MedicineId);

// Tombstone for a mistaken fact (D8): stock entry, count, intake or
// suspension. Wins whatever the arrival order.
public sealed record FactRetracted(Guid OpId, Hlc Hlc, Guid MedicineId, Guid FactId)
    : Operation(OpId, Hlc, MedicineId);

// Genesis content (§3.5): frozen rows existing when the Phase 2 patch
// ran, the cutoff day and the epoch reached at that moment.
public sealed record LegacyMovement(Guid OpId, Hlc Hlc, Guid MedicineId, Guid FactId, StockMovementKind Kind, decimal Delta, DateTimeOffset OccurredAt)
    : Operation(OpId, Hlc, MedicineId);

public sealed record LegacyIntake(Guid OpId, Hlc Hlc, Guid MedicineId, Guid IntakeId, DateOnly Day, IntakeStatus Status, decimal Quantity)
    : Operation(OpId, Hlc, MedicineId);

public sealed record LegacyBaseline(Guid OpId, Hlc Hlc, Guid MedicineId, DateOnly CutoffDay, int Epoch)
    : Operation(OpId, Hlc, MedicineId);
