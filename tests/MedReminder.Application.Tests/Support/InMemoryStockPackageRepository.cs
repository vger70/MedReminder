using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryStockPackageRepository : IStockPackageRepository
{
    private readonly List<StockPackage> _items = new();

    public IReadOnlyList<StockPackage> All => _items;

    public Task<StockPackage?> GetAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_items.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<StockPackage>> ListForMedicineAsync(Guid medicineId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockPackage>>(_items.Where(p => p.MedicineId == medicineId).ToList());

    public Task AddAsync(StockPackage package, CancellationToken cancellationToken)
    {
        _items.Add(package);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(StockPackage package, CancellationToken cancellationToken)
    {
        var index = _items.FindIndex(p => p.Id == package.Id);
        if (index >= 0) _items[index] = package;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(StockPackage package, CancellationToken cancellationToken)
    {
        _items.RemoveAll(p => p.Id == package.Id);
        return Task.CompletedTask;
    }

    public void RemoveForMedicine(Guid medicineId) => _items.RemoveAll(p => p.MedicineId == medicineId);
}

internal sealed class InMemoryPackageExpiryNoticeEventRepository : IPackageExpiryNoticeEventRepository
{
    private readonly List<PackageExpiryNoticeEvent> _items = new();

    public IReadOnlyList<PackageExpiryNoticeEvent> All => _items;

    public Task<bool> ExistsAsync(Guid packageId, DateOnly effectiveExpiry, int stage, CancellationToken cancellationToken)
        => Task.FromResult(_items.Any(e => e.PackageId == packageId && e.EffectiveExpiry == effectiveExpiry && e.Stage == stage));

    public Task AddAsync(PackageExpiryNoticeEvent notice, CancellationToken cancellationToken)
    {
        _items.Add(notice);
        return Task.CompletedTask;
    }

    public void RemoveForMedicine(Guid medicineId) => _items.RemoveAll(e => e.MedicineId == medicineId);
}
