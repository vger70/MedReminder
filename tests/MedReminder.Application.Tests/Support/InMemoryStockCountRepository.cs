using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryStockCountRepository : IStockCountRepository
{
    private readonly List<StockCount> _items = new();

    public IReadOnlyList<StockCount> All => _items;

    public Task<IReadOnlyList<StockCount>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        IReadOnlyList<StockCount> result =
            _items.Where(c => c.MedicineId == medicineId).OrderBy(c => c.RecordedAt).ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(StockCount count, CancellationToken cancellationToken)
    {
        _items.Add(count);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(StockCount count, CancellationToken cancellationToken)
    {
        _items.RemoveAll(c => c.Id == count.Id);
        return Task.CompletedTask;
    }
}
