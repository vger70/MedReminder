using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.UseCases;

// Adds positive stock (new package, manual addition, upward
// correction). Increments the medicine's StockEpoch: after a refill,
// the warning cycle restarts (spec §8, ANALYSIS §1.1 item 4).
public sealed record AddStockCommand(
    Guid MedicineId,
    decimal Quantity,
    StockMovementKind Kind,
    string? Notes = null);

public sealed class AddStock
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public AddStock(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _stock = stock;
        _uow = uow;
        _clock = clock;
    }

    public async Task ExecuteAsync(AddStockCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.Quantity <= 0m)
            throw new ArgumentException("Quantity must be positive.", nameof(cmd));
        if (!IsPositiveKind(cmd.Kind))
            throw new ArgumentException($"Movement kind {cmd.Kind} is not allowed for a positive stock load.", nameof(cmd));

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        medicine.StockEpoch += 1;
        medicine.UpdatedAt = _clock.GetUtcNow();

        var movement = new StockMovement
        {
            MedicineId = medicine.Id,
            OccurredAt = _clock.GetUtcNow(),
            Kind = cmd.Kind,
            QuantityDelta = cmd.Quantity,
            StockEpoch = medicine.StockEpoch,
            Notes = string.IsNullOrWhiteSpace(cmd.Notes) ? null : cmd.Notes.Trim(),
        };

        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _stock.AddAsync(movement, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    private static bool IsPositiveKind(StockMovementKind kind) => kind
        is StockMovementKind.NewPackage
        or StockMovementKind.ManualAdd
        or StockMovementKind.PositiveCorrection;
}
