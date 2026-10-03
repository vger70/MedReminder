namespace MedReminder.Domain.Stock;

// Which packages of a medicine are still in the cabinet (docs/analysis/
// ANALYSIS-PACKAGE-EXPIRY.md §3.4). Users rarely mark a package finished,
// so the current stock decides: it is assumed to sit in the packages
// consumed last. Pure and recomputed on every read, never stored, so it
// follows every stock count, retraction and remote fact by itself.
public static class PackageAllocation
{
    // The stock each open package holds, by package id; 0 means the
    // package is used up. Closed packages are left out.
    //
    // Stock not covered by any package (legacy stock, manual additions)
    // is assumed to be consumed after the tracked packages: the stock is
    // given to them first, which errs toward warning about a package
    // rather than hiding one.
    public static IReadOnlyDictionary<Guid, decimal> Allocate(
        IEnumerable<StockPackage> packages, decimal currentStock)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var remaining = Math.Max(currentStock, 0m);
        var allocated = new Dictionary<Guid, decimal>();
        foreach (var package in ConsumptionOrder(packages).Reverse())
        {
            var share = Math.Min(Math.Max(package.Quantity, 0m), remaining);
            allocated[package.Id] = share;
            remaining -= share;
        }
        return allocated;
    }

    // The open packages in the order they are assumed to be used: opened
    // ones first (the one in use), then sealed ones first expiring, first
    // out (no expiry last). RecordedAt and Id make the order total.
    public static IReadOnlyList<StockPackage> ConsumptionOrder(IEnumerable<StockPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return [.. packages
            .Where(p => !p.IsClosed)
            .OrderBy(p => p.OpenedOn is null)
            .ThenBy(p => p.OpenedOn ?? DateOnly.MaxValue)
            .ThenBy(p => PackageExpiryRules.EffectiveExpiry(p) ?? DateOnly.MaxValue)
            .ThenBy(p => p.RecordedAt)
            .ThenBy(p => p.Id)];
    }
}
