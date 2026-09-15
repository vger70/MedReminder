using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class StockMovementRepository : IStockMovementRepository
{
    private readonly MedReminderDbContext _db;

    public StockMovementRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<StockMovement>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        return await _db.StockMovements
            .AsNoTracking()
            .Where(m => m.MedicineId == medicineId)
            .OrderBy(m => m.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        await _db.StockMovements.AddAsync(movement, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken cancellationToken)
    {
        await _db.StockMovements.AddRangeAsync(movements, cancellationToken);
    }

    public async Task<DateOnly?> GetLastConsumptionDayAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        // Il provider SQLite di EF Core 10 non traduce Max()/Min() su
        // DateTimeOffset (viene mappato a TEXT ISO 8601 con offset e le
        // aggregate non hanno una traduzione sicura sulla stringa).
        // Uso OrderByDescending + FirstOrDefault che è invece traducibile
        // e produce un TOP 1 sull'indice (MedicineId, Kind, OccurredAt).
        var latest = await _db.StockMovements
            .AsNoTracking()
            .Where(m => m.MedicineId == medicineId
                        && m.Kind == StockMovementKind.Consumption)
            .OrderByDescending(m => m.OccurredAt)
            .Select(m => (DateTimeOffset?)m.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        return latest.HasValue ? DateOnly.FromDateTime(latest.Value.DateTime) : null;
    }
}
