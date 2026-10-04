using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Packages;

// What the user enters for a package (docs/analysis/
// ANALYSIS-PACKAGE-EXPIRY.md §3.1). Id null records a new one. Marking a
// package finished is a save with ClosedOn and PackageClosure.Finished;
// clearing both reopens it.
public sealed record SaveStockPackageCommand(
    Guid? Id,
    Guid MedicineId,
    decimal Quantity,
    DateOnly? ExpiresOn,
    int? UseWithinDays,
    DateOnly? OpenedOn,
    string? Batch,
    DateOnly? ClosedOn = null,
    PackageClosure? Closure = null,
    Guid? MovementId = null);

public sealed class InvalidStockPackageException : Exception
{
    public InvalidStockPackageException(PackageError error)
        : base($"The package is not valid: {error}.")
    {
        Error = error;
    }

    public PackageError Error { get; }
}

// Records or changes a package. The whole package is one replicated
// register (PackageChanged), so every save writes its full state.
public sealed class SaveStockPackage
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockPackageRepository _packages;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public SaveStockPackage(
        IMedicineRepository medicines,
        IStockPackageRepository packages,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _packages = packages;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    public Task<Guid> ExecuteAsync(SaveStockPackageCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return WriteGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    // Callers hold WriteGate.
    internal async Task<Guid> ExecuteCoreAsync(SaveStockPackageCommand cmd, CancellationToken ct)
    {
        _ = await _medicines.GetAsync(cmd.MedicineId, ct)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");
        var now = _clock.GetUtcNow();

        StockPackage package;
        var isNew = cmd.Id is null;
        if (isNew)
        {
            package = new StockPackage { MedicineId = cmd.MedicineId, RecordedAt = now };
        }
        else
        {
            package = await _packages.GetAsync(cmd.Id!.Value, ct)
                ?? throw new InvalidOperationException($"Package {cmd.Id} not found.");
            if (package.MedicineId != cmd.MedicineId)
                throw new InvalidOperationException($"Package {cmd.Id} belongs to another medicine.");
        }

        // Discarding moves the stock, so it goes through DiscardStockPackage
        // only, and it is final: a save neither sets nor clears it, which
        // also lets a sync merge keep a discard whatever other edit wins.
        var wasDiscarded = package.Closure == PackageClosure.Discarded;
        if (!wasDiscarded && cmd.Closure == PackageClosure.Discarded)
            throw new InvalidOperationException("A package is discarded with DiscardStockPackage, not saved as discarded.");
        if (wasDiscarded && (cmd.Closure != PackageClosure.Discarded || cmd.ClosedOn != package.ClosedOn))
            throw new InvalidOperationException("A discarded package stays discarded.");

        package.MovementId = isNew ? cmd.MovementId : cmd.MovementId ?? package.MovementId;
        package.Quantity = cmd.Quantity;
        package.ExpiresOn = cmd.ExpiresOn;
        package.UseWithinDays = cmd.UseWithinDays;
        package.OpenedOn = cmd.OpenedOn;
        package.Batch = string.IsNullOrWhiteSpace(cmd.Batch) ? null : cmd.Batch.Trim();
        package.ClosedOn = cmd.ClosedOn;
        package.Closure = cmd.Closure;
        package.UpdatedAt = now;
        if (PackageExpiryRules.Validate(package, LocalDay(_clock)) is { } error)
            throw new InvalidStockPackageException(error);

        if (isNew) await _packages.AddAsync(package, ct);
        else await _packages.UpdateAsync(package, ct);
        await _operations.AppendAsync([Operations.Package(package, deleted: false)], ct);
        await _uow.SaveChangesAsync(ct);
        return package.Id;
    }

    internal static DateOnly LocalDay(TimeProvider clock)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone).DateTime);
}

// A package thrown away (§3.5): it is closed as Discarded on a day and
// the units still in it leave the stock as a negative correction. Both
// are saved together; everything is checked before anything changes.
// Final: a discard cannot be undone (SaveStockPackage refuses it), so a
// discard by mistake is corrected by deleting the package and adding the
// units back. QuantityLeft 0: the package was already empty.
public sealed record DiscardStockPackageCommand(
    Guid PackageId,
    DateOnly DiscardedOn,
    decimal QuantityLeft,
    string? Notes = null);

public sealed class DiscardStockPackage
{
    private readonly IStockPackageRepository _packages;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly AdjustStockDown _adjust;
    private readonly TimeProvider _clock;

    public DiscardStockPackage(
        IStockPackageRepository packages,
        IOperationLog operations,
        IUnitOfWork uow,
        AdjustStockDown adjust,
        TimeProvider clock)
    {
        _packages = packages;
        _operations = operations;
        _uow = uow;
        _adjust = adjust;
        _clock = clock;
    }

    public Task ExecuteAsync(DiscardStockPackageCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.QuantityLeft < 0m)
            throw new ArgumentException("The quantity left cannot be negative.", nameof(cmd));
        return WriteGate.RunExclusiveAsync(async ct =>
        {
            var package = await _packages.GetAsync(cmd.PackageId, ct)
                ?? throw new InvalidOperationException($"Package {cmd.PackageId} not found.");
            if (package.IsClosed)
                throw new InvalidOperationException($"Package {cmd.PackageId} is already closed.");

            // Everything is checked before the tracked package changes, so a
            // refused discard leaves it as it was.
            var candidate = new StockPackage
            {
                MedicineId = package.MedicineId,
                Quantity = package.Quantity,
                ExpiresOn = package.ExpiresOn,
                UseWithinDays = package.UseWithinDays,
                OpenedOn = package.OpenedOn,
                Batch = package.Batch,
                ClosedOn = cmd.DiscardedOn,
                Closure = PackageClosure.Discarded,
            };
            if (PackageExpiryRules.Validate(candidate, SaveStockPackage.LocalDay(_clock)) is { } error)
                throw new InvalidStockPackageException(error);
            var correction = cmd.QuantityLeft > 0m
                ? await _adjust.PrepareAsync(
                    new AdjustStockDownCommand(package.MedicineId, cmd.QuantityLeft, cmd.Notes), ct)
                : null;

            package.ClosedOn = cmd.DiscardedOn;
            package.Closure = PackageClosure.Discarded;
            package.UpdatedAt = _clock.GetUtcNow();
            await _packages.UpdateAsync(package, ct);
            await _operations.AppendAsync([Operations.Package(package, deleted: false)], ct);
            if (correction is not null) await _adjust.RecordAsync(correction, ct);
            await _uow.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);
    }
}

// Removes a package entered by mistake. The stock does not change: a
// package never moves the stock by itself.
public sealed class DeleteStockPackage
{
    private readonly IStockPackageRepository _packages;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public DeleteStockPackage(
        IStockPackageRepository packages,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _packages = packages;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    public Task ExecuteAsync(Guid packageId, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            var package = await _packages.GetAsync(packageId, ct);
            if (package is null) return true;
            package.UpdatedAt = _clock.GetUtcNow();
            await _packages.RemoveAsync(package, ct);
            await _operations.AppendAsync([Operations.Package(package, deleted: true)], ct);
            await _uow.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);
}
