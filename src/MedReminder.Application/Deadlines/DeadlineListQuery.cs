using MedReminder.Application.Abstractions;
using MedReminder.Domain.Deadlines;

namespace MedReminder.Application.Deadlines;

// One deadline with the medicine it belongs to (null for a deadline of
// the profile), for the list.
public sealed record DeadlineListItem(
    Deadline Deadline,
    string? MedicineName,
    DeadlineStatus Status);

// Read-only query behind Therapy → Deadlines…: every deadline of the
// profile, the ones to act on first (overdue, due soon, upcoming, done),
// the nearest date first within each group (the latest done first).
public sealed class DeadlineListQuery
{
    private readonly IDeadlineRepository _deadlines;
    private readonly IMedicineRepository _medicines;
    private readonly TimeProvider _clock;

    public DeadlineListQuery(IDeadlineRepository deadlines, IMedicineRepository medicines, TimeProvider clock)
    {
        _deadlines = deadlines;
        _medicines = medicines;
        _clock = clock;
    }

    public DateOnly LocalToday()
    {
        var local = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public async Task<IReadOnlyList<DeadlineListItem>> LoadAsync(CancellationToken cancellationToken)
    {
        var today = LocalToday();
        var medicines = (await _medicines.ListAllAsync(cancellationToken)).ToDictionary(m => m.Id, m => m.Name);
        return (await _deadlines.ListAllAsync(cancellationToken))
            .Select(d => new DeadlineListItem(
                d,
                d.MedicineId is { } id ? medicines.GetValueOrDefault(id) : null,
                d.StatusOn(today)))
            .OrderBy(i => i.Status)
            .ThenByDescending(i => i.Deadline.DoneOn)
            .ThenBy(i => i.Deadline.DueOn)
            .ThenBy(i => i.MedicineName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
