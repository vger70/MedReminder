using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class StockMovementRepository : IStockMovementRepository
{
    private readonly MedReminderDbContext _db;
    private readonly TimeProvider _clock;

    // TimeProvider is optional (defaults to TimeProvider.System) so
    // integration tests that instantiate the repo without a DI scope
    // do not break.
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
        // The EF Core 10 SQLite provider does not translate Max() /
        // Min() on DateTimeOffset (mapped to ISO 8601 TEXT with
        // offset; aggregates have no safe translation on the
        // string). OrderByDescending + FirstOrDefault is translatable
        // and produces a TOP 1 on the (MedicineId, Kind, OccurredAt)
        // index.
        var latest = await _db.StockMovements
            .AsNoTracking()
            .Where(m => m.MedicineId == medicineId
                        && m.Kind == StockMovementKind.Consumption)
            .OrderByDescending(m => m.OccurredAt)
            .Select(m => (DateTimeOffset?)m.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (!latest.HasValue) return null;

        // After the DateTimeOffset ↔ long value converter, the read
        // value is always in zero offset (UTC). Convert back to the
        // local zone to get the "day" on which the movement actually
        // happened — consistent with how ConsumptionCatchUp writes
        // events (local midday in the local zone).
        var local = TimeZoneInfo.ConvertTime(latest.Value, _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
