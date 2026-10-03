using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryPendingDataMigrations : IPendingDataMigrations
{
    public HashSet<string> Pending { get; } = new(StringComparer.Ordinal);

    public Task<bool> IsPendingAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(Pending.Contains(name));

    public Task CompleteAsync(string name, CancellationToken cancellationToken)
    {
        Pending.Remove(name);
        return Task.CompletedTask;
    }
}
