using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class SyncConflictRepository : ISyncConflictRepository
{
    private readonly MedReminderDbContext _db;

    public SyncConflictRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SyncConflict>> ListForSubjectAsync(
        Guid subjectId, CancellationToken cancellationToken)
        => await _db.SyncConflicts.AsNoTracking()
            .Where(c => c.SubjectId == subjectId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SyncConflict>> ListAllAsync(CancellationToken cancellationToken)
        => await _db.SyncConflicts.AsNoTracking().ToListAsync(cancellationToken);

    public async Task AddAsync(SyncConflict conflict, CancellationToken cancellationToken)
        => await _db.SyncConflicts.AddAsync(conflict, cancellationToken);

    // Removes a row read without tracking, or one added in this unit of
    // work (then it is simply not inserted).
    public Task RemoveAsync(SyncConflict conflict, CancellationToken cancellationToken)
    {
        var tracked = _db.SyncConflicts.Local.FirstOrDefault(c => c.Id == conflict.Id);
        _db.SyncConflicts.Remove(tracked ?? conflict);
        return Task.CompletedTask;
    }
}
