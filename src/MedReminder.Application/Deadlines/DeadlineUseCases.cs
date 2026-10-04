using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Notifications;

namespace MedReminder.Application.Deadlines;

// What the user enters for a deadline. Id null records a new one.
public sealed record SaveDeadlineCommand(
    Guid? Id,
    Guid? MedicineId,
    DeadlineKind Kind,
    string? Label,
    DateOnly DueOn,
    int LeadDays,
    int? RepeatMonths,
    NotificationChannels Channels,
    DateOnly? DoneOn);

public sealed class InvalidDeadlineException : Exception
{
    public InvalidDeadlineException(DeadlineError error)
        : base($"The deadline is not valid: {error}.")
    {
        Error = error;
    }

    public DeadlineError Error { get; }
}

// Records or changes a deadline (docs/notes/EVOLUTION-PROPOSALS-2.md
// §3.6). The whole deadline is one replicated register (DeadlineChanged),
// so every save writes its full state.
public sealed class SaveDeadline
{
    private readonly IMedicineRepository _medicines;
    private readonly IDeadlineRepository _deadlines;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public SaveDeadline(
        IMedicineRepository medicines,
        IDeadlineRepository deadlines,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _deadlines = deadlines;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    public Task<Guid> ExecuteAsync(SaveDeadlineCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return WriteGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    // Callers hold WriteGate.
    internal async Task<Guid> ExecuteCoreAsync(SaveDeadlineCommand cmd, CancellationToken ct)
    {
        if (cmd.MedicineId is { } medicineId)
        {
            _ = await _medicines.GetAsync(medicineId, ct)
                ?? throw new InvalidOperationException($"Medicine {medicineId} not found.");
        }
        var now = _clock.GetUtcNow();

        Deadline deadline;
        var isNew = cmd.Id is null;
        if (isNew)
        {
            deadline = new Deadline { RecordedAt = now };
        }
        else
        {
            deadline = await _deadlines.GetAsync(cmd.Id!.Value, ct)
                ?? throw new InvalidOperationException($"Deadline {cmd.Id} not found.");
        }

        deadline.MedicineId = cmd.MedicineId;
        deadline.Kind = cmd.Kind;
        deadline.Label = string.IsNullOrWhiteSpace(cmd.Label) ? null : cmd.Label.Trim();
        deadline.DueOn = cmd.DueOn;
        deadline.LeadDays = cmd.LeadDays;
        deadline.RepeatMonths = cmd.RepeatMonths;
        deadline.Channels = cmd.Channels;
        deadline.DoneOn = cmd.DoneOn;
        deadline.UpdatedAt = now;
        if (DeadlineRules.Validate(deadline) is { } error) throw new InvalidDeadlineException(error);

        if (isNew) await _deadlines.AddAsync(deadline, ct);
        else await _deadlines.UpdateAsync(deadline, ct);
        await _operations.AppendAsync([Operations.Deadline(deadline, deleted: false)], ct);
        await _uow.SaveChangesAsync(ct);
        return deadline.Id;
    }
}

// Marks a deadline done on a day: a one-off deadline is closed, a
// recurring one moves to its next date (DeadlineRules.Complete).
public sealed class CompleteDeadline
{
    private readonly IDeadlineRepository _deadlines;
    private readonly SaveDeadline _save;

    public CompleteDeadline(IDeadlineRepository deadlines, SaveDeadline save)
    {
        _deadlines = deadlines;
        _save = save;
    }

    public Task ExecuteAsync(Guid deadlineId, DateOnly doneOn, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            var current = await _deadlines.GetAsync(deadlineId, ct)
                ?? throw new InvalidOperationException($"Deadline {deadlineId} not found.");
            // Computed on a copy: the save writes the tracked row.
            var next = new Deadline
            {
                Id = current.Id,
                DueOn = current.DueOn,
                RepeatMonths = current.RepeatMonths,
                DoneOn = current.DoneOn,
            };
            DeadlineRules.Complete(next, doneOn);
            return await _save.ExecuteCoreAsync(new SaveDeadlineCommand(
                current.Id, current.MedicineId, current.Kind, current.Label, next.DueOn, current.LeadDays,
                current.RepeatMonths, current.Channels, next.DoneOn), ct);
        }, cancellationToken);
}

public sealed class DeleteDeadline
{
    private readonly IDeadlineRepository _deadlines;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public DeleteDeadline(
        IDeadlineRepository deadlines,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _deadlines = deadlines;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    public Task ExecuteAsync(Guid deadlineId, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            var deadline = await _deadlines.GetAsync(deadlineId, ct);
            if (deadline is null) return true;
            deadline.UpdatedAt = _clock.GetUtcNow();
            await _deadlines.RemoveAsync(deadline, ct);
            await _operations.AppendAsync([Operations.Deadline(deadline, deleted: true)], ct);
            await _uow.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);
}
