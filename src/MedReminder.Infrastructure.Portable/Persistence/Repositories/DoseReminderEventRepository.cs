using MedReminder.Application.Abstractions;
using MedReminder.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class DoseReminderEventRepository
    : IDoseReminderEventRepository
{
    private readonly MedReminderDbContext _db;

    public DoseReminderEventRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(
        Guid medicineId,
        string slotKey,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        return _db.DoseReminderEvents
            .AsNoTracking()
            .AnyAsync(
                e => e.MedicineId == medicineId
                  && e.SlotKey == slotKey
                  && e.LocalDate == localDate,
                cancellationToken);
    }

    // On the unique (MedicineId, SlotKey, LocalDate) index.
    public async Task<IReadOnlySet<string>> ListFiredAsync(
        Guid medicineId, DateOnly localDate, IReadOnlyCollection<string> slotKeys, CancellationToken cancellationToken)
    {
        if (slotKeys.Count == 0) return new HashSet<string>(StringComparer.Ordinal);
        var keys = slotKeys.Distinct(StringComparer.Ordinal).ToList();
        var fired = await _db.DoseReminderEvents
            .AsNoTracking()
            .Where(e => e.MedicineId == medicineId && e.LocalDate == localDate && keys.Contains(e.SlotKey))
            .Select(e => e.SlotKey)
            .ToListAsync(cancellationToken);
        return fired.ToHashSet(StringComparer.Ordinal);
    }

    public async Task AddAsync(DoseReminderEvent evt, CancellationToken cancellationToken)
    {
        await _db.DoseReminderEvents.AddAsync(evt, cancellationToken);
    }

    public Task PruneOlderThanAsync(DateOnly cutoff, CancellationToken cancellationToken)
    {
        return _db.DoseReminderEvents
            .Where(e => e.LocalDate < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
