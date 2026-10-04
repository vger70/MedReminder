using MedReminder.Domain.Deadlines;

namespace MedReminder.Application.Abstractions;

// Administrative deadlines of the profile (docs/notes/
// EVOLUTION-PROPOSALS-2.md §3.6).
public interface IDeadlineRepository
{
    Task<Deadline?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Deadline>> ListAllAsync(CancellationToken cancellationToken);

    Task AddAsync(Deadline deadline, CancellationToken cancellationToken);

    Task UpdateAsync(Deadline deadline, CancellationToken cancellationToken);

    Task RemoveAsync(Deadline deadline, CancellationToken cancellationToken);
}

// Deadline reminders shown by this device (not replicated), keyed on
// (DeadlineId, DueOn).
public interface IDeadlineReminderEventRepository
{
    Task<bool> ExistsAsync(Guid deadlineId, DateOnly dueOn, CancellationToken cancellationToken);

    Task AddAsync(DeadlineReminderEvent reminder, CancellationToken cancellationToken);
}
