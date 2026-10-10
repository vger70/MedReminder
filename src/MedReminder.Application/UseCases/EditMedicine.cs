using MedReminder.Application.Abstractions;

namespace MedReminder.Application.UseCases;

// An edit of a medicine from a form: the general fields (UpdateMedicine)
// and, when the form changed it, the schedule (ChangeMedicationSchedule,
// which keeps the timeline). Both run in one transaction, so a failed
// schedule change does not leave the general fields saved without it.
public sealed record EditMedicineCommand(
    UpdateMedicineCommand Update,
    ChangeMedicationScheduleCommand? ScheduleChange = null);

public sealed class EditMedicine
{
    private readonly UpdateMedicine _update;
    private readonly ChangeMedicationSchedule _changeSchedule;
    private readonly ITransactionalScope _transaction;

    public EditMedicine(UpdateMedicine update, ChangeMedicationSchedule changeSchedule, ITransactionalScope transaction)
    {
        _update = update;
        _changeSchedule = changeSchedule;
        _transaction = transaction;
    }

    public Task ExecuteAsync(EditMedicineCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        UpdateMedicine.Validate(cmd.Update);
        if (cmd.ScheduleChange is { } change)
        {
            if (change.MedicineId != cmd.Update.MedicineId)
                throw new ArgumentException("The schedule change belongs to another medicine.", nameof(cmd));
            ChangeMedicationSchedule.Validate(change);
        }

        // One gate for the whole edit, taken before the transaction: no
        // other gated writer (catch-up, monitor, sync apply, database swap)
        // runs between the two steps, and the SQLite write lock is always
        // taken after the gate, never before it.
        return WriteGate.RunExclusiveAsync(gated => _transaction.RunAsync(async ct =>
        {
            await _update.ExecuteCoreAsync(cmd.Update, ct);
            if (cmd.ScheduleChange is { } scheduleChange)
                await _changeSchedule.ExecuteCoreAsync(scheduleChange, ct);
        }, gated), cancellationToken);
    }
}
