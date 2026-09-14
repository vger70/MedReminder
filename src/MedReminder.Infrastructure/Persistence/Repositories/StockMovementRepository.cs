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
        // Il giorno del movimento è la .Date dell'OccurredAt salvato nel
        // fuso locale a 12:00 (vedi Application/ConsumptionCatchUp).
        // EF Core traduce Max su DateTimeOffset a un aggregato SQL:
        // in-memory dopo la ToList potrebbe essere più semplice per la
        // versione MVP, ma su volumi normali (poche migliaia di record)
        // la Max lato DB è più efficiente.
        var maxUtc = await _db.StockMovements
            .AsNoTracking()
            .Where(m => m.MedicineId == medicineId
                        && m.Kind == StockMovementKind.Consumption)
            .Select(m => (DateTimeOffset?)m.OccurredAt)
            .MaxAsync(cancellationToken);

        return maxUtc is null ? null : DateOnly.FromDateTime(maxUtc.Value.DateTime);
    }
}
