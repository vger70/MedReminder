using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemorySyncConflictRepository : ISyncConflictRepository
{
    private readonly List<SyncConflict> _items = new();

    public IReadOnlyList<SyncConflict> All => _items;

    public Task<IReadOnlyList<SyncConflict>> ListForSubjectAsync(Guid subjectId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SyncConflict>>([.. _items.Where(c => c.SubjectId == subjectId)]);

    public Task<IReadOnlyList<SyncConflict>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SyncConflict>>([.. _items]);

    public Task AddAsync(SyncConflict conflict, CancellationToken cancellationToken)
    {
        _items.Add(conflict);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(SyncConflict conflict, CancellationToken cancellationToken)
    {
        _items.RemoveAll(c => c.Id == conflict.Id);
        return Task.CompletedTask;
    }
}
