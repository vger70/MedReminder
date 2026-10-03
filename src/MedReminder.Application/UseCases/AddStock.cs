using MedReminder.Application.Abstractions;
using MedReminder.Application.Packages;
using MedReminder.Application.Sync;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.UseCases;

// Adds positive stock (new package, manual addition, upward
// correction). Increments the medicine's StockEpoch: after a refill,
// the warning cycle restarts (spec §8, ANALYSIS §1.1 item 4).
//
// Packages, when given with a NewPackage load, records the boxes it
// brings in (docs/analysis/ANALYSIS-PACKAGE-EXPIRY.md §5.1), linked to
// the movement and saved with it.
public sealed record AddStockCommand(
    Guid MedicineId,
    decimal Quantity,
    StockMovementKind Kind,
    string? Notes = null,
    NewPackagesInput? Packages = null);

// Count boxes sharing expiry, in-use period and batch; the quantity of
// the load is split evenly between them. OpenedToday opens the first.
public sealed record NewPackagesInput(
    int Count,
    DateOnly? ExpiresOn,
    int? UseWithinDays,
    bool OpenedToday,
    string? Batch)
{
    public const int MaxCount = 99;
}

public sealed class AddStock
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly IStockPackageRepository? _packages;

    public AddStock(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock,
        IStockPackageRepository? packages = null)
    {
        _medicines = medicines;
        _stock = stock;
        _operations = operations;
        _uow = uow;
        _clock = clock;
        _packages = packages;
    }

    public async Task ExecuteAsync(AddStockCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.Quantity <= 0m)
            throw new ArgumentException("Quantity must be positive.", nameof(cmd));
        if (!IsPositiveKind(cmd.Kind))
            throw new ArgumentException($"Movement kind {cmd.Kind} is not allowed for a positive stock load.", nameof(cmd));
        if (cmd.Packages is { } packages)
        {
            if (cmd.Kind != StockMovementKind.NewPackage)
                throw new ArgumentException("Packages come only with a new-package load.", nameof(cmd));
            if (packages.Count is < 1 or > NewPackagesInput.MaxCount)
                throw new ArgumentException("The number of packages is out of range.", nameof(cmd));
            if (_packages is null)
                throw new InvalidOperationException("No package repository is available.");
        }
        await WriteGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    private async Task ExecuteCoreAsync(AddStockCommand cmd, CancellationToken cancellationToken)
    {

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        // Built and validated before any change, so a refused package
        // leaves the stock as it was.
        var movementId = Guid.NewGuid();
        var packages = cmd.Packages is { } input ? BuildPackages(cmd, input, movementId) : [];

        medicine.StockEpoch += 1;
        medicine.UpdatedAt = _clock.GetUtcNow();

        var movement = new StockMovement
        {
            Id = movementId,
            MedicineId = medicine.Id,
            OccurredAt = _clock.GetUtcNow(),
            Kind = cmd.Kind,
            QuantityDelta = cmd.Quantity,
            StockEpoch = medicine.StockEpoch,
            Origin = StockMovementOrigin.User,
            Notes = string.IsNullOrWhiteSpace(cmd.Notes) ? null : cmd.Notes.Trim(),
        };

        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _stock.AddAsync(movement, cancellationToken);
        await _operations.AppendAsync([Operations.StockEntry(movement)], cancellationToken);
        foreach (var package in packages)
        {
            await _packages!.AddAsync(package, cancellationToken);
        }
        if (packages.Count > 0)
        {
            await _operations.AppendAsync([.. packages.Select(p => Operations.Package(p, deleted: false))],
                cancellationToken);
        }
        await _uow.SaveChangesAsync(cancellationToken);
    }

    private List<StockPackage> BuildPackages(AddStockCommand cmd, NewPackagesInput input, Guid movementId)
    {
        var now = _clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _clock.LocalTimeZone).DateTime);
        var each = Math.Round(cmd.Quantity / input.Count, 2);
        var batch = string.IsNullOrWhiteSpace(input.Batch) ? null : input.Batch.Trim();
        var packages = new List<StockPackage>(input.Count);
        for (var i = 0; i < input.Count; i++)
        {
            var package = new StockPackage
            {
                MedicineId = cmd.MedicineId,
                MovementId = movementId,
                Quantity = each,
                ExpiresOn = input.ExpiresOn,
                UseWithinDays = input.UseWithinDays,
                OpenedOn = i == 0 && input.OpenedToday ? today : null,
                Batch = batch,
                // Distinct instants keep the order the boxes were entered in.
                RecordedAt = now.AddTicks(i),
                UpdatedAt = now,
            };
            if (PackageExpiryRules.Validate(package, today) is { } error)
                throw new InvalidStockPackageException(error);
            packages.Add(package);
        }
        return packages;
    }

    private static bool IsPositiveKind(StockMovementKind kind) => kind
        is StockMovementKind.NewPackage
        or StockMovementKind.ManualAdd
        or StockMovementKind.PositiveCorrection;
}
