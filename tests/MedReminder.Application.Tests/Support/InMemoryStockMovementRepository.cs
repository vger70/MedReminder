using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryStockMovementRepository : IStockMovementRepository
{
    private readonly List<StockMovement> _items = new();

    public IReadOnlyList<StockMovement> All => _items;

    public Task<IReadOnlyList<StockMovement>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        IReadOnlyList<StockMovement> result =
            _items.Where(m => m.MedicineId == medicineId).ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        _items.Add(movement);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken cancellationToken)
    {
        _items.AddRange(movements);
        return Task.CompletedTask;
    }

    public Task<DateOnly?> GetLastConsumptionDayAsync(Guid medicineId, CancellationToken cancellationToken)
    {
        var days = _items
            .Where(m => m.MedicineId == medicineId && m.Kind == StockMovementKind.Consumption)
            .Select(m => DateOnly.FromDateTime(m.OccurredAt.DateTime))
            .ToList();

        DateOnly? last = days.Count == 0 ? null : days.Max();
        return Task.FromResult(last);
    }
}
