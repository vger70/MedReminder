using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryUnitOfWork : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCalls++;
        return Task.FromResult(0);
    }
}
