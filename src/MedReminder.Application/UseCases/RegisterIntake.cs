using MedReminder.Application.Abstractions;
using MedReminder.Application.Ledger;
using MedReminder.Application.Sync;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.UseCases;

public sealed record RegisterIntakeCommand(
    Guid MedicineId,
    DateOnly Day,
    IntakeStatus Status,
    decimal Quantity,
    string? Notes = null,
    bool IsExtra = false);

// Records a single intake (spec §6) as a fact (B.1 Phase 2c-2): the
// MedicationIntake row, recorded now. The stock rows follow from the
// ledger derivation (LedgerSynchronizer, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §4.3), applied at once so stock decreases
// in real time:
//  - Taken: a Consumption of the quantity on the intake's day;
//  - any status: the day gets no automatic consumption. For a day
//    still derived, the automatic row simply goes away; for a day
//    carrying frozen (Legacy) consumption, the first intake books a
//    PositiveCorrection that reverses it, since frozen rows are never
//    edited.
//  - IsExtra (Taken only): an extra dose on top of the plan, typically
//    an as-needed one. It books its quantity and leaves the day's
//    automatic consumption in place (docs/analysis/
//    ANALYSIS-INTRADAY-CONSUMPTION.md §5.3).
// StockEpoch is not incremented: this is not a refill.
public sealed class RegisterIntake
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationIntakeRepository _intakes;
    private readonly LedgerSynchronizer _ledger;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public RegisterIntake(
        IMedicineRepository medicines,
        IMedicationIntakeRepository intakes,
        LedgerSynchronizer ledger,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _intakes = intakes;
        _ledger = ledger;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    // Runs under WriteGate: a catch-up interleaved between the
    // ledger read and the commit would derive from facts that miss this
    // intake.
    public Task<Guid> ExecuteAsync(RegisterIntakeCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return WriteGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    private async Task<Guid> ExecuteCoreAsync(RegisterIntakeCommand cmd, CancellationToken cancellationToken)
    {
        if (cmd.Quantity <= 0m)
            throw new ArgumentException("Intake quantity must be positive.", nameof(cmd));
        if (cmd.IsExtra && cmd.Status != IntakeStatus.Taken)
            throw new ArgumentException("Only a taken intake can be an extra dose.", nameof(cmd));

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        var now = _clock.GetUtcNow();
        var intake = new MedicationIntake
        {
            MedicineId = medicine.Id,
            Day = cmd.Day,
            Status = cmd.Status,
            Quantity = cmd.Quantity,
            ActualAt = cmd.Status == IntakeStatus.Taken ? now : null,
            Notes = string.IsNullOrWhiteSpace(cmd.Notes) ? null : cmd.Notes.Trim(),
            RecordedAt = now,
            IsExtra = cmd.IsExtra,
        };

        var before = await _ledger.LoadFactsAsync(medicine, cancellationToken);
        var after = before with
        {
            Intakes = [.. before.Intakes, new LedgerIntake(
                intake.Id, intake.Day, intake.Status, intake.Quantity, now, IsLegacy: false, intake.Notes,
                intake.IsExtra)],
        };

        if (cmd.Status == IntakeStatus.Taken)
        {
            // Sanity check: stock cannot drop below zero. The change of
            // the raw ledger includes the automatic consumption the
            // intake replaces on its day.
            var today = _ledger.LocalToday();
            var ledgerBefore = LedgerDeriver.Derive(before, today, _ledger.Zone);
            var ledgerAfter = LedgerDeriver.Derive(after, today, _ledger.Zone);
            var current = ledgerBefore.Stock + (ledgerAfter.RawTotal - ledgerBefore.RawTotal) + cmd.Quantity;
            if (MedicineStock.WouldGoNegative(current, -cmd.Quantity))
            {
                throw new InvalidOperationException(
                    $"The intake would push the stock below zero (current: {current}).");
            }
        }

        await _intakes.AddAsync(intake, cancellationToken);
        await _ledger.ApplyAsync(medicine, after, cancellationToken);

        medicine.UpdatedAt = now;
        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _operations.AppendAsync([Operations.Intake(intake)], cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return intake.Id;
    }
}
