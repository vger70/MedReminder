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

    // Data locale (DateOnly) del giorno più recente in cui esiste un
    // movimento di tipo Consumption per la medicina. Null se non ne esiste
    // nessuno: la Application interpreta come "materializza a partire dalla
    // StartDate della medicina".
    Task<DateOnly?> GetLastConsumptionDayAsync(
        Guid medicineId,
        CancellationToken cancellationToken);
}
