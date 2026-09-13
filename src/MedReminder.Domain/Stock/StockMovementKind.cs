namespace MedReminder.Domain.Stock;

// Tipi di movimento di magazzino (spec §17). I valori numerici sono
// espliciti perché persistiti su DB.
public enum StockMovementKind
{
    InitialLoad = 1,
    NewPackage = 2,
    ManualAdd = 3,
    Consumption = 4,
    PositiveCorrection = 5,
    NegativeCorrection = 6,
}
