using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class SyncFieldVersionRepository : ISyncFieldVersionRepository
{
    private readonly MedReminderDbContext _db;

    public SyncFieldVersionRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SyncFieldVersion>> ListForEntityAsync(
        Guid entityId, CancellationToken cancellationToken)
        => await _db.SyncFieldVersions.AsNoTracking()
            .Where(v => v.EntityId == entityId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SyncFieldVersion>> ListAllAsync(CancellationToken cancellationToken)
        => await _db.SyncFieldVersions.AsNoTracking().ToListAsync(cancellationToken);

    public async Task AddAsync(SyncFieldVersion version, CancellationToken cancellationToken)
        => await _db.SyncFieldVersions.AddAsync(version, cancellationToken);
}
