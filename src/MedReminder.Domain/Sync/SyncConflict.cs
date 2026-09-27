namespace MedReminder.Domain.Sync;

// Cases the conflict review shows (D7: the §4.5 list only,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md). The resolution is already
// applied; a conflict only records what lost, for review.
public enum SyncConflictKind
{
    // Same field of a medicine written concurrently on two devices.
    MedicineField = 1,
    // Two schedule rows with the same EffectiveFrom, written concurrently.
    ScheduleSameDate = 2,
    // The slots of a medicine replaced concurrently on two devices.
    SlotSetReplaced = 3,
    // Two suspensions from different devices cover a common day (hint).
    OverlappingSuspensions = 4,
    // Intakes of one day from different devices exceed the day's
    // scheduled quantity (hint).
    IntakesOverSchedule = 5,
    // A fact retracted on one device was edited on another.
    RetractedFactEdited = 6,
}

// One entry of the local conflict list (table SyncConflicts, B.1 Phase
// 3b). The id is a name-based GUID over the conflict's identity, so
// detecting the same conflict again never adds a second row.
//
// Register conflicts (MedicineField, ScheduleSameDate, SlotSetReplaced)
// are a function of the register's versions: every version concurrent
// with the winner lost to it. Devices holding the same versions list the
// same register conflicts. The hints (OverlappingSuspensions,
// IntakesOverSchedule, RetractedFactEdited) are raised when an incoming
// operation meets local state, so they are local notices.
public sealed class SyncConflict
{
    public required Guid Id { get; init; }

    public required SyncConflictKind Kind { get; init; }

    public required Guid MedicineId { get; init; }

    // The register entity, or the fact the hint is about.
    public required Guid SubjectId { get; init; }

    public string? Register { get; init; }

    public string? WinningValue { get; init; }

    public string? LosingValue { get; init; }

    public Guid? WinningDeviceId { get; init; }

    public Guid? LosingDeviceId { get; init; }

    // Id of the other fact of a hint (the overlapping suspension, the
    // other intake, the retraction).
    public Guid? OtherId { get; init; }

    public required DateTimeOffset DetectedAt { get; init; }
}
