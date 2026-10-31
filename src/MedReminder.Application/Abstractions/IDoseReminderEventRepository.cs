using MedReminder.Domain.Notifications;

namespace MedReminder.Application.Abstractions;

// Port for the DoseReminderEvents dedup table (ANALYSIS-A5 §3.2).
// Separate from INotificationEventRepository: the two paths
// deduplicate on structurally different keys and must not share a
// table (ANALYSIS-A5 §4.6).
public interface IDoseReminderEventRepository
{
    // True when a row with the given (medicineId, slotKey, localDate)
    // already exists — the dedup check before firing.
    Task<bool> ExistsAsync(
        Guid medicineId,
        string slotKey,
        DateOnly localDate,
        CancellationToken cancellationToken);

    // The keys among `slotKeys` already fired for the medicine on
    // `localDate`: one read for every slot and repetition of a medicine
    // instead of one per key (issue #328).
    async Task<IReadOnlySet<string>> ListFiredAsync(
        Guid medicineId, DateOnly localDate, IReadOnlyCollection<string> slotKeys, CancellationToken cancellationToken)
    {
        var fired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in slotKeys)
        {
            if (await ExistsAsync(medicineId, key, localDate, cancellationToken)) fired.Add(key);
        }
        return fired;
    }

    Task AddAsync(DoseReminderEvent evt, CancellationToken cancellationToken);

    // Deletes rows whose LocalDate is older than the given cutoff
    // (retention prune, ANALYSIS-A5 §3.3).
    Task PruneOlderThanAsync(DateOnly cutoff, CancellationToken cancellationToken);
}
