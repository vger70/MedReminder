using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryHouseholdStore : IHouseholdStore
{
    private readonly List<HouseholdOperationRecord> _operations = new();
    private readonly List<HouseholdRegisterVersion> _registers = new();
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
}
