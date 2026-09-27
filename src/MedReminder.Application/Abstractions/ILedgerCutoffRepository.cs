using MedReminder.Domain.Stock;

namespace MedReminder.Application.Abstractions;

// The profile's ledger cutoff (B.1 §3.5). Written only by the boot
// patch and by the import, never by a use case.
public interface ILedgerCutoffRepository
{
    // Null for a profile whose data never needed freezing.
    Task<LedgerCutoff?> GetAsync(CancellationToken cancellationToken);
}
