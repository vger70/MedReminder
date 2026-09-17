namespace MedReminder.Domain.Stock;

// Stock movement: the current quantity of a medicine is the sum of its
// movements (spec §17 and docs/ANALYSIS.md §2.4). Immutable: corrections
// are new movements, not edits to the past.
//
// Sign convention on QuantityDelta:
//   InitialLoad, NewPackage, ManualAdd, PositiveCorrection  -> >= 0
//   Consumption, NegativeCorrection                         -> <= 0
// The constraint is enforced by the Application layer at write time;
// the Domain does not enforce it so it can still read any inconsistent
// historical data present in the DB.
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
