using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.UseCases;

// Correzione negativa manuale (scorta erroneamente contata, medicina
// rovinata, ecc.). Non incrementa StockEpoch: non è un rifornimento.
public sealed record AdjustStockDownCommand(
    Guid MedicineId,
    decimal Quantity,   // positiva; verrà scritta come delta negativo
    string? Notes = null);

public sealed class AdjustStockDown
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public AdjustStockDown(
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

    public async Task ExecuteAsync(AdjustStockDownCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.Quantity <= 0m)
            throw new ArgumentException("La quantità da sottrarre deve essere positiva.", nameof(cmd));

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicina {cmd.MedicineId} non trovata.");

        var movements = await _stock.ListForMedicineAsync(cmd.MedicineId, cancellationToken);
        var current = MedicineStock.Current(movements);
        if (MedicineStock.WouldGoNegative(current, -cmd.Quantity))
        {
            throw new InvalidOperationException(
                $"La correzione porterebbe la scorta sotto zero (attuale: {current}).");
        }

        var movement = new StockMovement
        {
            MedicineId = medicine.Id,
            OccurredAt = _clock.GetUtcNow(),
            Kind = StockMovementKind.NegativeCorrection,
            QuantityDelta = -cmd.Quantity,
            StockEpoch = medicine.StockEpoch,
            Notes = string.IsNullOrWhiteSpace(cmd.Notes) ? null : cmd.Notes.Trim(),
        };

        await _stock.AddAsync(movement, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
