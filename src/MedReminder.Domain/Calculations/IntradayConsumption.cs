using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Quantity of today's scheduled doses whose time of day has passed
// (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md §4). The ledger books
// a day's automatic consumption only once the day is over, so during
// the day the stock is the start-of-day value; the main list shows that
// value minus this quantity. A read-side estimate: nothing is stored.
//
// Time of a dose:
//  - slot with Time: that time;
//  - slot without Time: the time of its preset (presetTime), none when
//    the preset is unknown or has no time;
//  - quantity of a slot: DailyConsumption.SlotQuantities (its dose, or
//    its share of the quantity of a non-FixedDaily schedule);
//  - no slots, FixedDaily with 1 to 4 administrations: today's quantity
//    in equal doses at the default times of that count;
//  - no slots, other non-PRN schedules: today's quantity at the default
//    time of a single administration.
// A dose without a time stays in the end-of-day booking and is never
// due here. As-needed slots are never due. Comparison is on local
// wall-clock time, so a time skipped by a DST change is due from the
// first instant after it.
//
// Zero when today is already booked: an intake that handles the day
// (not an extra one) or a stock count that materialized it. Capped at
// today's planned quantity, which is what the ledger books at midnight.
public static class IntradayConsumption
{
    public static decimal DueSoFar(
        Medicine medicine,
        DateOnly today,
        TimeOnly now,
        IEnumerable<MedicationScheduleHistory> scheduleHistory,
        IEnumerable<MedicationSuspension> suspensions,
        IReadOnlyList<MedicationAdministrationSlot> slots,
        bool dayAlreadyBooked,
        Func<Guid, TimeOnly?> presetTime,
        IReadOnlyDictionary<int, IReadOnlyList<TimeOnly>> defaultTimes)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        ArgumentNullException.ThrowIfNull(scheduleHistory);
        ArgumentNullException.ThrowIfNull(suspensions);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(presetTime);
        ArgumentNullException.ThrowIfNull(defaultTimes);

        if (!medicine.IsActive || dayAlreadyBooked) return 0m;

        var schedule = scheduleHistory.ToList();
        var planned = ConsumptionMaterializer
            .Plan(medicine, today, today, schedule, suspensions, slots)
            .Sum(p => p.Quantity);
        if (planned <= 0m) return 0m;

        decimal due;
        if (slots.Count > 0)
        {
            due = 0m;
            foreach (var (slot, quantity) in DailyConsumption.SlotQuantities(today, schedule, slots))
            {
                if (slot.IsAsNeeded) continue;
                var time = slot.Time ?? (slot.PresetId is { } id ? presetTime(id) : null);
                if (time is { } t && t <= now) due += quantity;
            }
        }
        else
        {
            var times = DefaultTimes(today, schedule, defaultTimes);
            if (times.Count == 0) return 0m;
            var perDose = planned / times.Count;
            due = times.Count(t => t <= now) * perDose;
        }

        return Math.Min(due, planned);
    }

    // Times of the doses of a medicine without slots; empty when they
    // cannot be placed (more than the configured counts).
    private static IReadOnlyList<TimeOnly> DefaultTimes(
        DateOnly today,
        IReadOnlyList<MedicationScheduleHistory> schedule,
        IReadOnlyDictionary<int, IReadOnlyList<TimeOnly>> defaultTimes)
    {
        if (DailyConsumption.RowInForce(today, schedule) is not { } inForce) return [];

        var count = inForce.ScheduleKind == ScheduleKind.FixedDaily ? inForce.AdministrationsPerDay : 1;
        return defaultTimes.TryGetValue(count, out var times) && times.Count == count ? times : [];
    }
}
