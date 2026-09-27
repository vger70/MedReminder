using MedReminder.Application.Abstractions;
using MedReminder.Domain.Ledger;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryFactRetractionRepository : IFactRetractionRepository
{
    private readonly List<FactRetraction> _items = new();

    public IReadOnlyList<FactRetraction> All => _items;

    public Task<IReadOnlyList<FactRetraction>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        IReadOnlyList<FactRetraction> result = _items.Where(r => r.MedicineId == medicineId).ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(FactRetraction retraction, CancellationToken cancellationToken)
    {
        _items.Add(retraction);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(FactRetraction retraction, CancellationToken cancellationToken)
    {
        _items.RemoveAll(r => r.Id == retraction.Id);
        return Task.CompletedTask;
    }
}
