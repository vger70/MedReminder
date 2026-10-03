using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Packages;

// One package as the lists show it (docs/analysis/
// ANALYSIS-PACKAGE-EXPIRY.md §5.3): its effective expiry, the stock the
// allocation gives it and its status today.
public sealed record PackageListItem(
    StockPackage Package,
    DateOnly? EffectiveExpiry,
    decimal Allocated,
    PackageExpiryStatus Status)
{
    // Expired or expiring soon: the package needs the user's attention.
    public bool NeedsAttention => Status is PackageExpiryStatus.Expired or PackageExpiryStatus.ExpiringSoon;
}

public sealed record NewPackageDefaults(int? UseWithinDays, decimal? Quantity);

// Read side of the packages of a medicine. Lead days are the defaults
// until the profile settings exist (P3).
public sealed class PackageListQuery
{
    private readonly IStockPackageRepository _packages;
    private readonly IStockMovementRepository _stock;
    private readonly TimeProvider _clock;

    public PackageListQuery(IStockPackageRepository packages, IStockMovementRepository stock, TimeProvider clock)
    {
        _packages = packages;
        _stock = stock;
        _clock = clock;
    }

    public DateOnly LocalToday()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone).DateTime);

    // Every package of the medicine, the ones to act on first.
    public async Task<IReadOnlyList<PackageListItem>> LoadAsync(Guid medicineId, CancellationToken cancellationToken)
    {
        var packages = await _packages.ListForMedicineAsync(medicineId, cancellationToken);
        var stock = MedicineStock.Current(await _stock.ListForMedicineAsync(medicineId, cancellationToken));
        return Build(packages, stock, LocalToday(), PackageLeadDays.Default);
    }

    // The in-use period and size of the medicine's latest package: the
    // defaults of the next one (§3.3). Both null without packages.
    public async Task<NewPackageDefaults> NewPackageDefaultsAsync(Guid medicineId, CancellationToken cancellationToken)
    {
        var latest = (await _packages.ListForMedicineAsync(medicineId, cancellationToken))
            .MaxBy(p => p.RecordedAt);
        return new NewPackageDefaults(latest?.UseWithinDays, latest?.Quantity);
    }

    // Status order, then effective expiry (none last).
    public static IReadOnlyList<PackageListItem> Build(
        IEnumerable<StockPackage> packages, decimal currentStock, DateOnly today, PackageLeadDays leadDays)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var list = packages.ToList();
        var allocated = PackageAllocation.Allocate(list, currentStock);
        return [.. list
            .Select(p =>
            {
                var share = allocated.GetValueOrDefault(p.Id);
                return new PackageListItem(p, PackageExpiryRules.EffectiveExpiry(p), share,
                    PackageExpiryRules.StatusOn(p, today, share, leadDays));
            })
            .OrderBy(i => i.Status)
            .ThenBy(i => i.EffectiveExpiry ?? DateOnly.MaxValue)
            .ThenBy(i => i.Package.RecordedAt)];
    }

    // The package of the medicine that expires first among those still in
    // stock, for the main list; null when none has an expiry.
    public static PackageListItem? NextExpiring(IEnumerable<PackageListItem> items)
        => items
            .Where(i => i.EffectiveExpiry is not null
                && i.Status is PackageExpiryStatus.Expired or PackageExpiryStatus.ExpiringSoon or PackageExpiryStatus.Valid)
            .MinBy(i => i.EffectiveExpiry);
}
