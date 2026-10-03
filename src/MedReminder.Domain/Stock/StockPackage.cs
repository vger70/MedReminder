namespace MedReminder.Domain.Stock;

// One physical package of a medicine in the cabinet (docs/analysis/
// ANALYSIS-PACKAGE-EXPIRY.md §3.1): its printed expiry, the in-use
// period after opening, when it was opened and when it left the cabinet.
// A parallel inventory: the stock quantity stays the sum of the stock
// movements, and a package never changes it by itself.
//
// Mutable, unlike StockMovement: opening, finishing and discarding are
// later changes of state. The batch is never logged.
public sealed class StockPackage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    // The NewPackage or InitialLoad movement that brought the package
    // in. Weak link: null for a package recorded afterwards, and left
    // dangling when that movement is retracted.
    public Guid? MovementId { get; set; }

    // Units in the package when full.
    public decimal Quantity { get; set; }

    // Printed expiry. A printed MM/YYYY is stored as the last day of the
    // month (PackageExpiryRules.EndOfMonth).
    public DateOnly? ExpiresOn { get; set; }

    // "Use within N days of first opening", the opening day being day 1.
    public int? UseWithinDays { get; set; }

    // First opening (for insulin in use, the first day out of the fridge).
    public DateOnly? OpenedOn { get; set; }

    // Lot as printed or scanned (GS1 AI 10).
    public string? Batch { get; set; }

    // Day the package left the cabinet; set together with Closure.
    public DateOnly? ClosedOn { get; set; }

    public PackageClosure? Closure { get; set; }

    public DateTimeOffset RecordedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsClosed => ClosedOn is not null;
}

// Why a package left the cabinet. Numeric values are explicit because
// they are persisted to the DB; member names are wire names in the sync
// format and in exports: do not rename them.
public enum PackageClosure
{
    Finished = 1,
    Discarded = 2,
}

// Order matters: lists show the packages to act on first.
public enum PackageExpiryStatus
{
    Expired,
    ExpiringSoon,
    Valid,
    UsedUp,
    Closed,
}
