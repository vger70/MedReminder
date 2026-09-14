using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.UseCases;

// Aggiunge scorta positiva (nuova confezione, aggiunta manuale, correzione
// in eccesso). Incrementa StockEpoch della medicina: dopo un rifornimento
// il ciclo di avviso riparte (spec §8, ANALYSIS §1.1 punto 4).
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
            throw new ArgumentException("La quantità deve essere positiva.", nameof(cmd));
        if (!IsPositiveKind(cmd.Kind))
            throw new ArgumentException($"Il tipo di movimento {cmd.Kind} non è ammesso per un carico positivo.", nameof(cmd));

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicina {cmd.MedicineId} non trovata.");

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
