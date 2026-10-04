using MedReminder.Application.Abstractions;

namespace MedReminder.Application;

// IDatabaseExclusiveAccess over WriteGate. Stateless: every instance
// shares the one process-wide gate.
public sealed class DatabaseExclusiveAccess : IDatabaseExclusiveAccess
{
    public Task RunExclusiveAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(action, cancellationToken);

    public Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(action, cancellationToken);
}
