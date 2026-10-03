using MedReminder.Domain.Stock;

namespace MedReminder.Application.Abstractions;

// Packages of the profile's medicines (docs/analysis/
// ANALYSIS-PACKAGE-EXPIRY.md §3.1).
public interface IStockPackageRepository
{
    Task<StockPackage?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<StockPackage>> ListForMedicineAsync(Guid medicineId, CancellationToken cancellationToken);

    Task AddAsync(StockPackage package, CancellationToken cancellationToken);

    Task UpdateAsync(StockPackage package, CancellationToken cancellationToken);

    Task RemoveAsync(StockPackage package, CancellationToken cancellationToken);
}
