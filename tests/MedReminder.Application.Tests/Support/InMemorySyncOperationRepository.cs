using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemorySyncOperationRepository : ISyncOperationRepository
{
    private readonly List<SyncOperation> _items = new();

    public IReadOnlyList<SyncOperation> All => _items;

    public Task AddAsync(SyncOperation operation, CancellationToken cancellationToken)
    {
        _items.Add(operation);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid operationId, CancellationToken cancellationToken)
        => Task.FromResult(_items.Any(o => o.Id == operationId));

    public Task<HybridTimestamp?> GetLatestTimestampAsync(CancellationToken cancellationToken)
        => Task.FromResult(_items.Count == 0 ? (HybridTimestamp?)null : _items.Max(o => o.Timestamp));

    public Task<IReadOnlyList<SyncOperation>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SyncOperation>>([.. _items.OrderBy(o => o.Timestamp)]);

    public Task<IReadOnlyList<SyncOperation>> ListForMedicineAsync(Guid medicineId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SyncOperation>>([.. _items.Where(o => o.MedicineId == medicineId).OrderBy(o => o.Timestamp)]);

    public Task<IReadOnlyList<SyncOperation>> ListPendingAsync(Guid deviceId, int generation, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SyncOperation>>([.. _items
            .Where(o => o.DeviceId == deviceId && o.Generation == generation && o.SegmentSeq is null)
            .OrderBy(o => o.Timestamp)]);

    public Task MarkPublishedAsync(IReadOnlyList<SyncOperation> operations, int segmentSeq, CancellationToken cancellationToken)
    {
        var ids = operations.Select(o => o.Id).ToHashSet();
        foreach (var o in _items.Where(o => ids.Contains(o.Id))) o.SegmentSeq = segmentSeq;
        return Task.CompletedTask;
    }

    public Task<long> CountAsync(int generation, CancellationToken cancellationToken)
        => Task.FromResult((long)_items.Count(o => o.Generation == generation));
}
