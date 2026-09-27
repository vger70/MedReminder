using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemorySyncFieldVersionRepository : ISyncFieldVersionRepository
{
    private readonly List<SyncFieldVersion> _items = new();

    public IReadOnlyList<SyncFieldVersion> All => _items;

    public Task<IReadOnlyList<SyncFieldVersion>> ListForEntityAsync(Guid entityId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SyncFieldVersion>>([.. _items.Where(v => v.EntityId == entityId)]);

    public Task<IReadOnlyList<SyncFieldVersion>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SyncFieldVersion>>([.. _items]);

    public Task AddAsync(SyncFieldVersion version, CancellationToken cancellationToken)
    {
        _items.Add(version);
        return Task.CompletedTask;
    }
}
