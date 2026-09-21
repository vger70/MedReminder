using MedReminder.Application.Abstractions;
using MedReminder.Domain.Notifications;

namespace MedReminder.Application.Tests.Support;

// In-memory stand-in for the DoseReminderEvents dedup table. Mirrors
// the real repository's key semantics: (MedicineId, SlotKey, LocalDate).
internal sealed class InMemoryDoseReminderEventRepository : IDoseReminderEventRepository
{
    private readonly List<DoseReminderEvent> _items = new();

    public IReadOnlyList<DoseReminderEvent> All => _items;

    public Task<bool> ExistsAsync(
        Guid medicineId, string slotKey, DateOnly localDate, CancellationToken cancellationToken)
    {
        var exists = _items.Any(e =>
            e.MedicineId == medicineId &&
            e.SlotKey == slotKey &&
            e.LocalDate == localDate);
        return Task.FromResult(exists);
    }

    public Task AddAsync(DoseReminderEvent evt, CancellationToken cancellationToken)
    {
        _items.Add(evt);
        return Task.CompletedTask;
    }

    public Task PruneOlderThanAsync(DateOnly cutoff, CancellationToken cancellationToken)
    {
        _items.RemoveAll(e => e.LocalDate < cutoff);
        return Task.CompletedTask;
    }
}
