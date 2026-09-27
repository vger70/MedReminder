namespace MedReminder.Domain.Stock;

// Where a stock movement comes from (B.1, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §3.4, §7.3). Numeric values are explicit
// because they are persisted to the DB.
//
//   Legacy  -> written before the B.1 Phase 2 boot patch (or imported
//              from an older archive): a frozen fact, never rewritten.
//   User    -> a fact the user asserted: initial load, new package,
//              manual add, positive / negative correction.
//   Derived -> computed from other facts: automatic and intake
//              consumption, backdated-intake reversals, stock-count
//              corrections. LedgerDeriver (Phase 2c) will recompute
//              these rows; until then the use cases still write them.
public enum StockMovementOrigin
{
    Legacy = 1,
    User = 2,
    Derived = 3,
}
