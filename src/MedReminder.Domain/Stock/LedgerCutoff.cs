namespace MedReminder.Domain.Stock;

// The profile's ledger cutoff (B.1, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §3.5). Written once, by the Phase 2b boot
// patch on a database that existed before it, or by the import of an
// older archive. Every stock movement present at that moment is marked
// Legacy; LedgerDeriver (Phase 2c) derives rows only for days after
// CutoffDay, so numbers up to the cutoff never change.
//
// A database created after the patch has no row: it has no legacy
// movements to protect.
public sealed class LedgerCutoff
{
    // Single-row table.
    public const int SingletonId = 1;

    public int Id { get; init; } = SingletonId;

    // Day before the patch, in the local time zone.
    public required DateOnly CutoffDay { get; init; }

    // Instant of the freeze. Movements recorded on the day after the
    // cutoff but before this instant are Legacy too; Phase 2c needs it
    // to avoid deriving them a second time.
    public required DateTimeOffset FrozenAt { get; init; }
}
