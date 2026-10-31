using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Domain.Deadlines;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class DeadlineRepository : IDeadlineRepository
{
    private readonly MedReminderDbContext _db;

    public DeadlineRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    // Tracked, so a later update in the same unit of work changes it.
    public async Task<Deadline?> GetAsync(Guid id, CancellationToken cancellationToken)
        => await _db.Deadlines.FindAsync([id], cancellationToken);

    public async Task<IReadOnlyList<Deadline>> ListAllAsync(CancellationToken cancellationToken)
        => await _db.Deadlines.AsNoTracking().ToListAsync(cancellationToken);

    public async Task AddAsync(Deadline deadline, CancellationToken cancellationToken)
        => await _db.Deadlines.AddAsync(deadline, cancellationToken);

    public Task UpdateAsync(Deadline deadline, CancellationToken cancellationToken)
    {
        if (_db.Entry(deadline).State == EntityState.Detached) _db.Deadlines.Update(deadline);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Deadline deadline, CancellationToken cancellationToken)
    {
        _db.Deadlines.Remove(deadline);
        return Task.CompletedTask;
    }
}

internal sealed class DeadlineReminderEventRepository : IDeadlineReminderEventRepository
{
    private readonly MedReminderDbContext _db;

    public DeadlineReminderEventRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(Guid deadlineId, DateOnly dueOn, CancellationToken cancellationToken)
        => _db.DeadlineReminderEvents.AnyAsync(e => e.DeadlineId == deadlineId && e.DueOn == dueOn, cancellationToken);

    // One read on the unique (DeadlineId, DueOn) index for every deadline
    // id, the exact keys matched in memory.
    public async Task<IReadOnlySet<DeadlineReminderKey>> ListRemindedAsync(
        IReadOnlyCollection<DeadlineReminderKey> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0) return new HashSet<DeadlineReminderKey>();
        var ids = keys.Select(k => k.DeadlineId).Distinct().ToList();
        var rows = await _db.DeadlineReminderEvents.AsNoTracking()
            .Where(e => ids.Contains(e.DeadlineId))
            .Select(e => new { e.DeadlineId, e.DueOn })
            .ToListAsync(cancellationToken);
        var wanted = keys.ToHashSet();
        return rows.Select(r => new DeadlineReminderKey(r.DeadlineId, r.DueOn)).Where(wanted.Contains).ToHashSet();
    }

    public async Task AddAsync(DeadlineReminderEvent reminder, CancellationToken cancellationToken)
        => await _db.DeadlineReminderEvents.AddAsync(reminder, cancellationToken);
}
