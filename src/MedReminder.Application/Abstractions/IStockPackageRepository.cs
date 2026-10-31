using MedReminder.Application.Notifications;
using MedReminder.Domain.Notifications;
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

// Package expiry notices shown by this device (not replicated), keyed on
// (PackageId, EffectiveExpiry, Stage, Channel).
public interface IPackageExpiryNoticeEventRepository
{
    Task<bool> ExistsAsync(Guid packageId, DateOnly effectiveExpiry, int stage, NotificationChannels channel,
        CancellationToken cancellationToken);

    // The keys among `keys` already shown on `channel`: one read for the
    // packages of a medicine instead of two per package (issue #328).
    async Task<IReadOnlySet<PackageNoticeKey>> ListShownAsync(
        IReadOnlyCollection<PackageNoticeKey> keys, NotificationChannels channel, CancellationToken cancellationToken)
    {
        var shown = new HashSet<PackageNoticeKey>();
        foreach (var key in keys)
        {
            if (await ExistsAsync(key.PackageId, key.EffectiveExpiry, key.Stage, channel, cancellationToken))
            {
                shown.Add(key);
            }
        }
        return shown;
    }

    Task AddAsync(PackageExpiryNoticeEvent notice, CancellationToken cancellationToken);
}
