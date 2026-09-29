namespace MedReminder.Domain.Ledger;

// Kinds of fact a user can retract (B.1 Phase 2d, D8,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2). Numeric values are
// persisted.
public enum FactKind
{
    StockEntry = 1,
    Intake = 2,
    StockCount = 3,
    Suspension = 4,
}
