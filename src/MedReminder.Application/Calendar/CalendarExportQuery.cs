using MedReminder.Application.Abstractions;
using MedReminder.Application.Deadlines;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Prescriptions;

namespace MedReminder.Application.Calendar;

// Read-only query behind Tools → Export calendar (docs/notes/
// EVOLUTION-PROPOSALS-2.md §3.7): from today on, for every active
// medicine the day to request the prescription (run-out date minus the
// warning threshold) and the run-out date, the last valid day of every
// prescription to collect and the date of every open deadline. Dates
// before today are left out. Writes nothing.
public sealed class CalendarExportQuery
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IPrescriptionRepository _prescriptions;
    private readonly IDeadlineRepository _deadlines;
    private readonly TimeProvider _clock;
    private readonly ILocalizationService? _localization;

    public CalendarExportQuery(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IMedicationAdministrationSlotRepository slots,
        IPrescriptionRepository prescriptions,
        IDeadlineRepository deadlines,
        TimeProvider clock,
        ILocalizationService? localization = null)
    {
        _medicines = medicines;
        _stock = stock;
        _schedules = schedules;
        _suspensions = suspensions;
        _slots = slots;
        _prescriptions = prescriptions;
        _deadlines = deadlines;
        _clock = clock;
        _localization = localization;
    }

    public DateOnly LocalToday()
    {
        var local = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    // The events in date order. includeNames puts the medicine names and
    // the deadline descriptions in the titles.
    public async Task<IReadOnlyList<CalendarEvent>> LoadAsync(bool includeNames, CancellationToken cancellationToken)
    {
        var today = LocalToday();
        var events = new List<CalendarEvent>();
        var all = await _medicines.ListAllAsync(cancellationToken);
        var names = all.ToDictionary(m => m.Id, m => m.Name);

        foreach (var m in all.Where(m => m.IsActive))
        {
            var movements = await _stock.ListForMedicineAsync(m.Id, cancellationToken);
            var schedule = await _schedules.ListForMedicineAsync(m.Id, cancellationToken);
            var suspensions = await _suspensions.ListForMedicineAsync(m.Id, cancellationToken);
            var slots = await _slots.ListForMedicineAsync(m.Id, cancellationToken);
            // Same forecast as the medicine list.
            var forecast = MedicineForecast.Compute(today, MedicineStock.Current(movements), schedule, slots, suspensions);
            if (forecast.RunOut.EstimatedRunOutDate is not { } runOut || runOut < today) continue;
            var name = includeNames ? m.Name : null;
            var reorder = runOut.AddDays(-m.ThresholdDays);
            if (reorder >= today) events.Add(CalendarEntries.Reorder(m.Id, reorder, name, _localization));
            events.Add(CalendarEntries.RunOut(m.Id, runOut, name, _localization));
        }

        foreach (var p in await _prescriptions.ListAllAsync(cancellationToken))
        {
            if (p.StatusOn(today) != PrescriptionStatus.ToCollect || p.ValidUntil is not { } until) continue;
            if (!names.TryGetValue(p.MedicineId, out var medicineName)) continue;
            events.Add(CalendarEntries.Prescription(p.Id, until, includeNames ? medicineName : null, _localization));
        }

        foreach (var d in await _deadlines.ListAllAsync(cancellationToken))
        {
            if (d.DoneOn is not null || d.DueOn < today) continue;
            var subject = includeNames
                ? DeadlineTexts.Subject(d, d.MedicineId is { } id ? names.GetValueOrDefault(id) : null, _localization)
                : null;
            events.Add(CalendarEntries.Deadline(d.Id, d.DueOn, subject, _localization));
        }

        return events.OrderBy(e => e.Date).ThenBy(e => e.Uid, StringComparer.Ordinal).ToList();
    }

    // The whole file, stamped now.
    public async Task<string> WriteAsync(bool includeNames, CancellationToken cancellationToken)
        => IcsWriter.Write(await LoadAsync(includeNames, cancellationToken), _clock.GetUtcNow());
}
