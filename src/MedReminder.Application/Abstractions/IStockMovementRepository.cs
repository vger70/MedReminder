using MedReminder.Domain.Stock;

namespace MedReminder.Application.Abstractions;

public interface IStockMovementRepository
{
    Task<IReadOnlyList<StockMovement>> ListForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    Task AddAsync(StockMovement movement, CancellationToken cancellationToken);

    Task AddRangeAsync(
        IEnumerable<StockMovement> movements,
        CancellationToken cancellationToken);

    // Local date (DateOnly) of the most recent day on which a
    // Consumption movement exists for the medicine. Null if none
    // exists: the Application interprets it as "materialize starting
    // from the medicine's StartDate".
    Task<DateOnly?> GetLastConsumptionDayAsync(
        Guid medicineId,
        CancellationToken cancellationToken);
}
