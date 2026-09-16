using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class StockMovementRepository : IStockMovementRepository
{
    private readonly MedReminderDbContext _db;
    private readonly TimeProvider _clock;

    // TimeProvider è opzionale (default TimeProvider.System) per non
    // rompere i test integrazione che istanziano il repo senza scope DI.
    public StockMovementRepository(MedReminderDbContext db, TimeProvider? clock = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
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

        if (!latest.HasValue) return null;

        // Dopo il value converter DateTimeOffset↔long il valore letto è
        // sempre in offset zero (UTC). Riconvertiamo al fuso locale per
        // ottenere il "giorno" nel quale il movimento è effettivamente
        // avvenuto — coerente con come ConsumptionCatchUp scrive gli
        // eventi (local midday nel fuso locale).
        var local = TimeZoneInfo.ConvertTime(latest.Value, _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
