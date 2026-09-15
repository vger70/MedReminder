using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.UseCases;

public sealed record RegisterIntakeCommand(
    Guid MedicineId,
    DateOnly Day,
    IntakeStatus Status,
    decimal Quantity,
    string? Notes = null);

// Registra una singola assunzione (spec §6). Sempre crea una riga in
// MedicationIntakes per audit; se lo status è Taken produce anche uno
// StockMovement Consumption con delta negativo pari alla quantità
// indicata — così la scorta scende in tempo reale, non aspettando il
// catch-up automatico. Il catch-up successivo salta il giorno grazie
// alla presenza dell'intake (vedi ConsumptionCatchUp).
public sealed class RegisterIntake
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IStockMovementRepository _stock;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public RegisterIntake(
        IMedicineRepository medicines,
        IMedicationIntakeRepository intakes,
        IStockMovementRepository stock,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _intakes = intakes;
        _stock = stock;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Guid> ExecuteAsync(RegisterIntakeCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.Quantity <= 0m)
            throw new ArgumentException("La quantità dell'assunzione deve essere positiva.", nameof(cmd));

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicina {cmd.MedicineId} non trovata.");

        var now = _clock.GetUtcNow();

        var intake = new MedicationIntake
        {
            MedicineId = medicine.Id,
            Day = cmd.Day,
            Status = cmd.Status,
            Quantity = cmd.Quantity,
            ActualAt = cmd.Status == IntakeStatus.Taken ? now : null,
            Notes = string.IsNullOrWhiteSpace(cmd.Notes) ? null : cmd.Notes.Trim(),
        };
        await _intakes.AddAsync(intake, cancellationToken);

        if (cmd.Status == IntakeStatus.Taken)
        {
            // Verifica di sanità: la scorta non può scendere sotto zero.
            var movements = await _stock.ListForMedicineAsync(cmd.MedicineId, cancellationToken);
            var current = MedicineStock.Current(movements);
            if (MedicineStock.WouldGoNegative(current, -cmd.Quantity))
            {
                throw new InvalidOperationException(
                    $"L'assunzione porterebbe la scorta sotto zero (attuale: {current}).");
            }

            var occurredAt = ToLocalMiddayOffset(cmd.Day);
            var movement = new StockMovement
            {
                MedicineId = medicine.Id,
                OccurredAt = occurredAt,
                Kind = StockMovementKind.Consumption,
                QuantityDelta = -cmd.Quantity,
                StockEpoch = medicine.StockEpoch,
                Notes = intake.Notes,
            };
            await _stock.AddAsync(movement, cancellationToken);
        }

        medicine.UpdatedAt = now;
        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return intake.Id;
    }

    private DateTimeOffset ToLocalMiddayOffset(DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(12, 0));
        var offset = _clock.LocalTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
