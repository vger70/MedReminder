using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryHouseholdStore : IHouseholdStore
{
    private readonly List<HouseholdOperationRecord> _operations = new();
    private readonly List<HouseholdRegisterVersion> _registers = new();
    private readonly Dictionary<Guid, int> _published = new();
    private readonly Dictionary<Guid, int> _applied = new();
    private HouseholdIdentity? _identity;

    public IReadOnlyList<HouseholdOperationRecord> Operations => _operations;

    public Task<HouseholdIdentity> EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        _identity ??= new HouseholdIdentity(Guid.NewGuid(), Guid.NewGuid(), 1);
        return Task.FromResult(_identity);
    }

    public Task<HybridTimestamp?> GetLatestTimestampAsync(CancellationToken cancellationToken)
        => Task.FromResult(_operations.Count == 0 ? (HybridTimestamp?)null : _operations.Max(o => o.Timestamp));

    public Task AppendAsync(HouseholdOperationRecord operation, IReadOnlyList<HouseholdRegisterVersion> writes,
        CancellationToken cancellationToken)
    {
        _operations.Add(operation);
        _registers.AddRange(writes);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HouseholdRegisterVersion>> ListRegistersAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<HouseholdRegisterVersion>>([.. _registers]);

    public Task<IReadOnlyList<HouseholdOperationRecord>> ListOperationsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<HouseholdOperationRecord>>([.. _operations.OrderBy(o => o.Timestamp)]);

    public Task<bool> ExistsAsync(Guid operationId, CancellationToken cancellationToken)
        => Task.FromResult(_operations.Any(o => o.Id == operationId));

    public Task<IReadOnlyList<HouseholdOperationRecord>> ListPendingAsync(Guid deviceId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<HouseholdOperationRecord>>(
            [.. _operations.Where(o => o.Timestamp.DeviceId == deviceId && !_published.ContainsKey(o.Id))]);

    public Task MarkPublishedAsync(IReadOnlyList<Guid> operationIds, int seq, CancellationToken cancellationToken)
    {
        foreach (var id in operationIds) _published[id] = seq;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<Guid, int>> GetAppliedAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>(_applied));

    public Task SetAppliedAsync(Guid deviceId, int seq, CancellationToken cancellationToken)
    {
        _applied[deviceId] = seq;
        return Task.CompletedTask;
    }

    public Task SaveIdentityAsync(HouseholdIdentity identity, CancellationToken cancellationToken)
    {
        _identity = identity;
        return Task.CompletedTask;
    }

    public Task ResetAsync(HouseholdIdentity identity, CancellationToken cancellationToken)
    {
        _operations.Clear();
        _registers.Clear();
        _published.Clear();
        _applied.Clear();
        _identity = identity;
        return Task.CompletedTask;
    }
}
