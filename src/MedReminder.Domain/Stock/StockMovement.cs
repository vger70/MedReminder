namespace MedReminder.Domain.Stock;

// Movimento di magazzino: la quantità corrente di una medicina è la
// somma dei suoi movimenti (spec §17 e docs/ANALYSIS.md §2.4).
// Immutabile: le correzioni sono nuovi movimenti, non modifiche al
// passato.
//
// Convenzione sul segno di QuantityDelta:
//   InitialLoad, NewPackage, ManualAdd, PositiveCorrection  -> >= 0
//   Consumption, NegativeCorrection                         -> <= 0
// Il vincolo è verificato dalla Application layer al momento della
// scrittura; il Domain non lo forza per non impedire la lettura di dati
// storici inconsistenti eventualmente presenti nel DB.
public sealed class StockMovement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required StockMovementKind Kind { get; init; }

    public required decimal QuantityDelta { get; init; }

    public required int StockEpoch { get; init; }

    public string? Notes { get; init; }
}
