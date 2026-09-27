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

    public Task<HybridTimestamp?> GetLatestTimestampAsync(CancellationToken cancellationToken)
        => Task.FromResult(_items.Count == 0 ? (HybridTimestamp?)null : _items.Max(o => o.Timestamp));

    public Task<IReadOnlyList<SyncOperation>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SyncOperation>>([.. _items.OrderBy(o => o.Timestamp)]);
}
