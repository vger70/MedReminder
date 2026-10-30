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
    bool IsExtra = false,
    DateTimeOffset? ScheduledAt = null,
    bool RequireSlotOpen = false);

// RequireSlotOpen (issue #321): the dose slot named by ScheduledAt is
// already answered by an intake of the day (DoseSlots.Records). Nothing
// was written.
public sealed class DoseSlotAlreadyRecordedException(Guid intakeId)
    : InvalidOperationException("The dose slot is already recorded.")
{
    // The intake that answers the slot.
    public Guid IntakeId { get; } = intakeId;
}

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
//  - ScheduledAt: the dose slot the intake answers (its time on Day), so
//    the reminder of that slot is not shown (DoseSlots.Records). Not for
//    an extra dose, which is on top of the plan.
//  - RequireSlotOpen (issue #321): refuse when the slot ScheduledAt
//    names is already answered, checked inside the gate, so two callers
//    (a notification action and the form) cannot both read the slot as
//    open and both record it. Without it a second intake for a covered
//    slot is recorded, as before.
// StockEpoch is not incremented: this is not a refill.
public sealed class RegisterIntake
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly LedgerSynchronizer _ledger;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public RegisterIntake(
        IMedicineRepository medicines,
        IMedicationIntakeRepository intakes,
        IMedicationAdministrationSlotRepository slots,
        LedgerSynchronizer ledger,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _intakes = intakes;
        _slots = slots;
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
        if (cmd.IsExtra && cmd.ScheduledAt is not null)
            throw new ArgumentException("An extra dose answers no dose slot.", nameof(cmd));
        // The slot time on Day, as the recording device reads it
        // (DoseSlots.Records): one on another day would answer no slot.
        if (cmd.ScheduledAt is { } at && DateOnly.FromDateTime(at.DateTime) != cmd.Day)
            throw new ArgumentException("The dose slot time is not on the intake day.", nameof(cmd));
        if (cmd.RequireSlotOpen && cmd.ScheduledAt is null)
            throw new ArgumentException("Only an intake for a dose slot can require it open.", nameof(cmd));

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        var now = _clock.GetUtcNow();
        // The latest recorded set, which Today and the reminders match
        // against, the set in force on the intake day, which the intake log
        // reads that day with, and the day's intakes: read once for the slot
        // time and the check.
        IReadOnlyList<MedicationAdministrationSlot> slots = [];
        IReadOnlyList<MedicationAdministrationSlot> daySlots = [];
        IReadOnlyList<MedicationIntake> day = [];
        if (cmd.ScheduledAt is not null)
        {
            slots = await _slots.ListForMedicineAsync(medicine.Id, cancellationToken);
            daySlots = InForce(await _slots.ListSetsForMedicineAsync(medicine.Id, cancellationToken), cmd.Day);
            day = await _intakes.ListForDayAsync(medicine.Id, cmd.Day, cancellationToken);
        }
        var intake = new MedicationIntake
        {
            MedicineId = medicine.Id,
            Day = cmd.Day,
            ScheduledAt = cmd.ScheduledAt,
            SlotTime = SlotTimeOf(cmd, daySlots, day) ?? SlotTimeOf(cmd, slots, day),
            Status = cmd.Status,
            Quantity = cmd.Quantity,
            ActualAt = cmd.Status == IntakeStatus.Taken ? now : null,
            Notes = string.IsNullOrWhiteSpace(cmd.Notes) ? null : cmd.Notes.Trim(),
            RecordedAt = now,
            IsExtra = cmd.IsExtra,
        };

        if (cmd.RequireSlotOpen) EnsureSlotOpen(intake, slots, day);

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

    // Among the slot sets in force on `day`, the most recently recorded
    // one, as the ledger reads a day (LedgerDeriver). Read in the order
    // LedgerFactsLoader gives the ledger, with the id as tie-break: the
    // store orders by RecordedAt alone, so two sets recorded at one
    // instant would otherwise resolve differently across devices.
    private static IReadOnlyList<MedicationAdministrationSlot> InForce(
        IReadOnlyList<AdministrationSlotSetEntry> sets, DateOnly day)
    {
        AdministrationSlotSetEntry? best = null;
        foreach (var entry in sets.OrderBy(e => e.Set.RecordedAt).ThenBy(e => e.Set.Id))
        {
            if (entry.Set.EffectiveFrom > day) continue;
            if (best is null || entry.Set.RecordedAt >= best.Set.RecordedAt) best = entry;
        }
        return best?.Slots ?? [];
    }

    // The time of the slot ScheduledAt names, read in this device's zone
    // as DoseSlots.Records reads it (issue #317): kept with the intake, so
    // a device in another zone matches the same slot. Null when it names
    // no slot (a time changed since), even with a single timed slot. Of two
    // slots at one instant (a time the clock skips moved onto another),
    // the one the intake takes after the day's intakes, as Records reads
    // it here: another zone, where the two instants differ, then reads
    // both slots as recorded too.
    private TimeOnly? SlotTimeOf(RegisterIntakeCommand cmd, IReadOnlyList<MedicationAdministrationSlot> slots,
        IReadOnlyList<MedicationIntake> day)
    {
        if (cmd.ScheduledAt is not { } scheduledAt) return null;
        var named = DoseSlots.NamedBy(cmd.Day, slots, scheduledAt, _ledger.Zone);
        if (named is null) return null;
        var probe = new MedicationIntake
        {
            MedicineId = cmd.MedicineId,
            Day = cmd.Day,
            ScheduledAt = scheduledAt,
            Status = cmd.Status,
            Quantity = cmd.Quantity,
            RecordedAt = DateTimeOffset.MaxValue,
        };
        var taken = DoseSlots.Records(cmd.Day, slots, [.. day, probe], _ledger.Zone)
            .FirstOrDefault(r => r.Value == probe).Key;
        return slots.FirstOrDefault(s => s.Id == taken)?.Time ?? named.Time;
    }

    // The slot the new intake names, read as DoseSlots.Records reads it
    // among the day's intakes: read last, it answers a slot only when no
    // other intake does. With several timed slots a ScheduledAt that
    // names none (changed since) is not refused; with a single timed slot
    // it names that slot (DoseSlots.Records) and is checked.
    private void EnsureSlotOpen(
        MedicationIntake intake, IReadOnlyList<MedicationAdministrationSlot> slots, IReadOnlyList<MedicationIntake> day)
    {
        var zone = _ledger.Zone;
        var named = DoseSlots.Records(intake.Day, slots, [intake], zone);
        if (named.Count == 0) return;
        // Records orders by RecordedAt, and an intake synced from another
        // device carries that device's clock, which can be ahead of this
        // one: the new intake is read last whatever the clocks say, so an
        // intake already stored always answers the slot first.
        var last = day.Count == 0 ? intake.RecordedAt : day.Max(i => i.RecordedAt);
        var probe = last <= intake.RecordedAt ? intake : new MedicationIntake
        {
            Id = intake.Id,
            MedicineId = intake.MedicineId,
            Day = intake.Day,
            ScheduledAt = intake.ScheduledAt,
            SlotTime = intake.SlotTime,
            Status = intake.Status,
            Quantity = intake.Quantity,
            IsExtra = intake.IsExtra,
            RecordedAt = last,
        };
        var records = DoseSlots.Records(intake.Day, slots, [.. day, probe], zone);
        if (records.Values.Any(i => i.Id == intake.Id)) return;
        var slotId = named.Keys.Single();
        throw new DoseSlotAlreadyRecordedException(records[slotId].Id);
    }
}
