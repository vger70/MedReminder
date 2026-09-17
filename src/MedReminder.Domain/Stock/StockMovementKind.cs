namespace MedReminder.Domain.Stock;

// Kinds of stock movement (spec §17). Numeric values are explicit
// because they are persisted to the DB.
public enum StockMovementKind
{
    InitialLoad = 1,
    NewPackage = 2,
    ManualAdd = 3,
    Consumption = 4,
    PositiveCorrection = 5,
    NegativeCorrection = 6,
}
