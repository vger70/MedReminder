namespace MedReminder.Domain.Stock;

// Rules of the package expiry (docs/analysis/ANALYSIS-PACKAGE-EXPIRY.md
// §3.2). A package expires on the earlier of its printed expiry and the
// last day of its in-use period after opening. Every date and period is
// entered by the user or scanned: there is no regulatory default.
public static class PackageExpiryRules
{
    public const int MaxUseWithinDays = 365;

    // GS1 AI 10 holds at most 20 characters.
    public const int MaxBatchLength = 20;

    public const decimal MaxQuantity = 100000m;

    // Last day a printed MM/YYYY expiry allows.
    public static DateOnly EndOfMonth(int year, int month)
        => new(year, month, DateTime.DaysInMonth(year, month));

    // Last day of use after opening, counting the opening day as day 1
    // (opened on 1 March, 28 days: 28 March), the conservative reading
    // of "use within N days". Null when not opened or with no period.
    public static DateOnly? InUseUntil(StockPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return package is { OpenedOn: { } opened, UseWithinDays: { } days }
            ? opened.AddDays(days - 1)
            : null;
    }

    // The earlier of the printed expiry and the end of the in-use
    // period; null when neither is known (the package is never warned).
    public static DateOnly? EffectiveExpiry(StockPackage package)
    {
        var printed = package.ExpiresOn;
        var inUse = InUseUntil(package);
        if (printed is null) return inUse;
        if (inUse is null) return printed;
        return inUse < printed ? inUse : printed;
    }

    // Whether the effective expiry comes from the opening, which takes
    // the shorter in-use lead.
    public static bool EndsWithInUsePeriod(StockPackage package)
        => InUseUntil(package) is { } inUse && (package.ExpiresOn is not { } printed || inUse < printed);

    // allocated: the stock the allocation gives the package
    // (PackageAllocation); 0 means the stock no longer covers it.
    public static PackageExpiryStatus StatusOn(
        StockPackage package, DateOnly today, decimal allocated, PackageLeadDays leadDays)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(leadDays);
        if (package.IsClosed) return PackageExpiryStatus.Closed;
        if (allocated <= 0m) return PackageExpiryStatus.UsedUp;
        if (EffectiveExpiry(package) is not { } expiry) return PackageExpiryStatus.Valid;
        if (today > expiry) return PackageExpiryStatus.Expired;
        var lead = EndsWithInUsePeriod(package) ? leadDays.InUse : leadDays.Printed;
        // A lead of 0 turns the "expiring soon" stage off.
        return lead > 0 && today >= expiry.AddDays(-lead)
            ? PackageExpiryStatus.ExpiringSoon
            : PackageExpiryStatus.Valid;
    }

    // Null when the entry is consistent, else the reason.
    public static PackageError? Validate(StockPackage package, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Quantity <= 0m || package.Quantity > MaxQuantity)
            return PackageError.Quantity;
        if (package.UseWithinDays is { } days && (days < 1 || days > MaxUseWithinDays))
            return PackageError.UseWithinDays;
        if (package.OpenedOn > today || package.ClosedOn > today)
            return PackageError.FutureDate;
        if ((package.ClosedOn is null) != (package.Closure is null))
            return PackageError.Closure;
        if (package.ClosedOn < package.OpenedOn)
            return PackageError.ClosedBeforeOpened;
        if (package.Batch is { Length: > MaxBatchLength })
            return PackageError.Batch;
        return null;
    }
}

// Days before the effective expiry when a package is "expiring soon":
// one lead for a printed expiry, a shorter one for the end of an in-use
// period (a 28-day period with a 30-day lead would warn the day the
// bottle is opened). Profile settings; 0 turns that stage off.
public sealed record PackageLeadDays(int Printed, int InUse)
{
    public const int DefaultPrinted = 30;
    public const int MaxPrinted = 180;
    public const int DefaultInUse = 3;
    public const int MaxInUse = 30;

    public static readonly PackageLeadDays Default = new(DefaultPrinted, DefaultInUse);

    // A stored value outside the range is clamped, never refused.
    public PackageLeadDays Clamped() => new(Math.Clamp(Printed, 0, MaxPrinted), Math.Clamp(InUse, 0, MaxInUse));
}

public enum PackageError
{
    Quantity,
    UseWithinDays,
    FutureDate,
    Closure,
    ClosedBeforeOpened,
    Batch,
}
