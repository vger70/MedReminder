using MedReminder.Application.Abstractions;
using MedReminder.Application.Monitoring;
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

// Records a single intake (spec §6). Always creates a row in
// MedicationIntakes for audit; if the status is Taken it also creates
// a Consumption StockMovement with a negative delta equal to the
// given quantity — so stock decreases in real time, without waiting
// for the automatic catch-up. The next catch-up skips the day thanks
// to the presence of the intake (see ConsumptionCatchUp).
//
// Backdated intakes: a past day may already carry the automatic
// consumption written by ConsumptionCatchUp. The first intake recorded
// for such a day (any status) replaces it, consistently with the
// catch-up rule that an intake day gets no automatic consumption: a
// PositiveCorrection movement reverses the automatic quantity, then
// the Taken quantity (if any) is booked as usual. The ledger stays
// append-only. StockEpoch is not incremented: this is not a refill.
// Consumption on a day without any intake is always automatic, since
// only ConsumptionCatchUp and this use case write Consumption.
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

    // Runs under MonitoringGate: a catch-up interleaved between the
    // ledger read and the commit could otherwise write the automatic
    // consumption for cmd.Day without it being reversed.
    public Task<Guid> ExecuteAsync(RegisterIntakeCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return MonitoringGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    private async Task<Guid> ExecuteCoreAsync(RegisterIntakeCommand cmd, CancellationToken cancellationToken)
    {
        if (cmd.Quantity <= 0m)
            throw new ArgumentException("Intake quantity must be positive.", nameof(cmd));

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        var now = _clock.GetUtcNow();

        // Read before adding the intake below: a prior intake means the
        // day's consumption is already manual and must not be reversed.
        var priorIntakeDays = await _intakes.ListManualIntakeDaysAsync(
            medicine.Id, cmd.Day, cmd.Day, cancellationToken);
        var movements = await _stock.ListForMedicineAsync(cmd.MedicineId, cancellationToken);
        var automaticQuantity = priorIntakeDays.Count > 0
            ? 0m
            : -movements
                .Where(m => m.Kind == StockMovementKind.Consumption
                            && LocalDay(m.OccurredAt) == cmd.Day)
                .Sum(m => m.QuantityDelta);

        if (cmd.Status == IntakeStatus.Taken)
        {
            // Sanity check: stock cannot drop below zero.
            var current = MedicineStock.Current(movements) + automaticQuantity;
            if (MedicineStock.WouldGoNegative(current, -cmd.Quantity))
            {
                throw new InvalidOperationException(
                    $"The intake would push the stock below zero (current: {current}).");
            }
        }

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

        var occurredAt = ToLocalMiddayOffset(cmd.Day);
        if (automaticQuantity > 0m)
        {
            await _stock.AddAsync(new StockMovement
            {
                MedicineId = medicine.Id,
                OccurredAt = occurredAt,
                Kind = StockMovementKind.PositiveCorrection,
                QuantityDelta = automaticQuantity,
                StockEpoch = medicine.StockEpoch,
            }, cancellationToken);
        }

        if (cmd.Status == IntakeStatus.Taken)
        {
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

    // Movements read back from SQLite carry a zero offset (UTC ticks);
    // convert to the local zone before taking the calendar day.
    private DateOnly LocalDay(DateTimeOffset occurredAt)
    {
        var local = TimeZoneInfo.ConvertTime(occurredAt, _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private DateTimeOffset ToLocalMiddayOffset(DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(12, 0));
        var offset = _clock.LocalTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
