using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class SyncOperationRepository : ISyncOperationRepository
{
    private readonly MedReminderDbContext _db;

    public SyncOperationRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(SyncOperation operation, CancellationToken cancellationToken)
        => await _db.SyncOperations.AddAsync(operation, cancellationToken);

    public Task<bool> ExistsAsync(Guid operationId, CancellationToken cancellationToken)
        => _db.SyncOperations.AnyAsync(o => o.Id == operationId, cancellationToken);

    // The device-id tie-break does not matter here: the clock only needs
    // the greatest physical time and counter.
    public async Task<HybridTimestamp?> GetLatestTimestampAsync(CancellationToken cancellationToken)
    {
        var latest = await _db.SyncOperations
            .AsNoTracking()
            .OrderByDescending(o => o.HlcPhysicalMs)
            .ThenByDescending(o => o.HlcCounter)
            .FirstOrDefaultAsync(cancellationToken);
        return latest?.Timestamp;
    }

    public async Task<IReadOnlyList<SyncOperation>> ListAllAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.SyncOperations.AsNoTracking().ToListAsync(cancellationToken);
        return [.. rows.OrderBy(o => o.Timestamp)];
    }

    public async Task<IReadOnlyList<SyncOperation>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        var rows = await _db.SyncOperations.AsNoTracking()
            .Where(o => o.MedicineId == medicineId)
            .ToListAsync(cancellationToken);
        return [.. rows.OrderBy(o => o.Timestamp)];
    }

    public async Task<IReadOnlyList<SyncOperation>> ListPendingAsync(
        Guid deviceId, int generation, CancellationToken cancellationToken)
    {
        var rows = await _db.SyncOperations.AsNoTracking()
            .Where(o => o.DeviceId == deviceId && o.Generation == generation && o.SegmentSeq == null)
            .ToListAsync(cancellationToken);
        return [.. rows.OrderBy(o => o.Timestamp)];
    }

    public async Task MarkPublishedAsync(
        IReadOnlyList<SyncOperation> operations, int segmentSeq, CancellationToken cancellationToken)
    {
        var ids = operations.Select(o => o.Id).ToList();
        await _db.SyncOperations
            .Where(o => ids.Contains(o.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.SegmentSeq, segmentSeq), cancellationToken);
    }

    public Task<long> CountAsync(int generation, CancellationToken cancellationToken)
        => _db.SyncOperations.LongCountAsync(o => o.Generation == generation, cancellationToken);
}
