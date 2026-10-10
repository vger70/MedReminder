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
        return _transaction.RunAsync(async ct =>
        {
            await _update.ExecuteAsync(cmd.Update, ct);
            if (cmd.ScheduleChange is { } change)
                await _changeSchedule.ExecuteAsync(change, ct);
        }, cancellationToken);
    }
}
