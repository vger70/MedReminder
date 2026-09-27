using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Tests.Support;

// Settable by tests to simulate a database frozen by the boot patch.
internal sealed class InMemoryLedgerCutoffRepository : ILedgerCutoffRepository
{
    public LedgerCutoff? Cutoff { get; set; }

    public Task<LedgerCutoff?> GetAsync(CancellationToken cancellationToken)
        => Task.FromResult(Cutoff);
}
