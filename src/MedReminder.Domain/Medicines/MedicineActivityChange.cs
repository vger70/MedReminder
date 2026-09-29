namespace MedReminder.Domain.Medicines;

// A dated activation or deactivation of a medicine (B.1 Phase 2c-2,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2, D15): "from Day on the
// medicine is active / inactive". Medicine.IsActive keeps the current
// value; this history lets the ledger derivation skip the days on which
// the medicine was inactive, also after a reactivation.
public sealed class MedicineActivityChange
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required DateOnly Day { get; init; }

    public required bool Active { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }
}
