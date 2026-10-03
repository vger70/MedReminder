using MedReminder.Application.Abstractions;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class StockPackageRepository : IStockPackageRepository
{
    private readonly MedReminderDbContext _db;

    public StockPackageRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    // Tracked, so a later update in the same unit of work changes it.
    public async Task<StockPackage?> GetAsync(Guid id, CancellationToken cancellationToken)
        => await _db.StockPackages.FindAsync([id], cancellationToken);

    public async Task<IReadOnlyList<StockPackage>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
        => await _db.StockPackages.AsNoTracking()
            .Where(p => p.MedicineId == medicineId)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(StockPackage package, CancellationToken cancellationToken)
        => await _db.StockPackages.AddAsync(package, cancellationToken);

    public Task UpdateAsync(StockPackage package, CancellationToken cancellationToken)
    {
        if (_db.Entry(package).State == EntityState.Detached) _db.StockPackages.Update(package);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(StockPackage package, CancellationToken cancellationToken)
    {
        _db.StockPackages.Remove(package);
        return Task.CompletedTask;
    }
}

internal sealed class PackageExpiryNoticeEventRepository : IPackageExpiryNoticeEventRepository
{
    private readonly MedReminderDbContext _db;

    public PackageExpiryNoticeEventRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(Guid packageId, DateOnly effectiveExpiry, int stage, NotificationChannels channel,
        CancellationToken cancellationToken)
        => _db.PackageExpiryNoticeEvents.AnyAsync(
            e => e.PackageId == packageId && e.EffectiveExpiry == effectiveExpiry && e.Stage == stage
                && e.Channel == channel, cancellationToken);

    public async Task AddAsync(PackageExpiryNoticeEvent notice, CancellationToken cancellationToken)
        => await _db.PackageExpiryNoticeEvents.AddAsync(notice, cancellationToken);
}
