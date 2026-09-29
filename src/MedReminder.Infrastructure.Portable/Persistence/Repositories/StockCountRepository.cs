using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class StockCountRepository : IStockCountRepository
{
    private readonly MedReminderDbContext _db;

    public StockCountRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<StockCount>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
        => await _db.StockCounts
            .AsNoTracking()
            .Where(c => c.MedicineId == medicineId)
            .OrderBy(c => c.RecordedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(StockCount count, CancellationToken cancellationToken)
        => await _db.StockCounts.AddAsync(count, cancellationToken);

    public Task RemoveAsync(StockCount count, CancellationToken cancellationToken)
    {
        _db.StockCounts.Remove(count);
        return Task.CompletedTask;
    }
}
