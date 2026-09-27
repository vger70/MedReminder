using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class SyncPeerRepository : ISyncPeerRepository
{
    private readonly MedReminderDbContext _db;

    public SyncPeerRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SyncPeer>> ListAllAsync(CancellationToken cancellationToken)
        => await _db.SyncPeers.AsNoTracking().ToListAsync(cancellationToken);

    public async Task AddAsync(SyncPeer peer, CancellationToken cancellationToken)
        => await _db.SyncPeers.AddAsync(peer, cancellationToken);

    // Rows come from AsNoTracking reads, one instance per read: an
    // instance already tracked in this unit of work takes the values.
    public Task UpdateAsync(SyncPeer peer, CancellationToken cancellationToken)
    {
        var tracked = _db.SyncPeers.Local.FirstOrDefault(p => p.DeviceId == peer.DeviceId);
        if (tracked is null)
        {
            _db.SyncPeers.Update(peer);
        }
        else if (!ReferenceEquals(tracked, peer))
        {
            _db.Entry(tracked).CurrentValues.SetValues(peer);
        }
        return Task.CompletedTask;
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        foreach (var local in _db.SyncPeers.Local.ToList()) _db.Entry(local).State = EntityState.Detached;
        await _db.SyncPeers.ExecuteDeleteAsync(cancellationToken);
    }
}
