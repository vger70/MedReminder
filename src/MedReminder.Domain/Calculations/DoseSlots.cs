using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// The timed dose slots of one medicine on one day and what was recorded
// for them (mobile issues #126, #127). An intake covers a slot when:
//  - it is not an extra dose (an extra one is on top of the plan);
//  - its ScheduledAt is the slot's time on that day: first as the wall
//    clock it was recorded with (the device that recorded it named that
//    slot, whatever this device's zone), then read in this zone, then as
//    the instant the slot fires (AtLocal). The database keeps ScheduledAt
//    as UTC ticks and reads it back with a zero offset, so a zero offset
//    names no recorded wall clock: that value is read in this zone only; or
//  - the medicine has a single timed slot, the only one it can mean:
//    without ScheduledAt (recorded from the medicine, not from a dose) or
//    with one that no longer matches (the slot time changed since).
// Any status covers the slot: taken, skipped and cancelled all answer
// the reminder. With several slots an intake without a matching
// ScheduledAt covers none: which dose it was is not known.
public static class DoseSlots
{
    // Timed slots that get a reminder: as-needed slots are taken only
    // when needed (ANALYSIS-INTRADAY-CONSUMPTION.md §5.1).
    public static IReadOnlyList<MedicationAdministrationSlot> Timed(IEnumerable<MedicationAdministrationSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        return [.. slots.Where(s => s.Time.HasValue && !s.IsAsNeeded).OrderBy(s => s.Time).ThenBy(s => s.Order)];
    }

    // The intake recorded for each covered slot of `day`; the first one
    // by recording instant when there are several.
    public static IReadOnlyDictionary<Guid, MedicationIntake> Records(
        DateOnly day,
        IEnumerable<MedicationAdministrationSlot> slots,
        IEnumerable<MedicationIntake> intakes,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(intakes);
        ArgumentNullException.ThrowIfNull(zone);
        var timed = Timed(slots);
        var result = new Dictionary<Guid, MedicationIntake>();
        if (timed.Count == 0) return result;

        foreach (var intake in intakes.Where(i => i.Day == day && !i.IsExtra).OrderBy(i => i.RecordedAt))
        {
            MedicationAdministrationSlot? slot = null;
            if (intake.ScheduledAt is { } at)
            {
                var inZone = TimeZoneInfo.ConvertTime(at, zone);
                slot = (at.Offset == TimeSpan.Zero ? null : timed.FirstOrDefault(s => SameMinute(at.DateTime, day, s.Time!.Value)))
                    ?? timed.FirstOrDefault(s => SameMinute(inZone.DateTime, day, s.Time!.Value))
                    ?? timed.FirstOrDefault(s => at == AtLocal(day, s.Time!.Value, zone));
                // Two slots can fire at the same instant (a time the clock
                // skips moves to the next valid one): an intake whose slot is
                // already covered takes the other slot of that instant.
                if (slot is not null && result.ContainsKey(slot.Id))
                {
                    slot = timed.FirstOrDefault(s => !result.ContainsKey(s.Id) && at == AtLocal(day, s.Time!.Value, zone))
                        ?? slot;
                }
            }
            // A single timed slot is the only dose an intake of the day can
            // mean, with or without a ScheduledAt that still matches it (the
            // slot time may have changed after the intake was recorded).
            slot ??= timed.Count == 1 ? timed[0] : null;
            if (slot is not null) result.TryAdd(slot.Id, intake);
        }
        return result;
    }

    // The state of each timed slot of `day` at `now`. Empty when no dose
    // is due that day: outside the therapy window, no rate, suspended, or
    // no stock at the start of the day (the DoseReminderService gates).
    // A slot without an intake is Due from its time until `dueWindow`
    // has passed, then NotRecorded.
    public static IReadOnlyList<DoseSlotStatus> StatesOn(
        DateOnly day,
        DateTimeOffset now,
        TimeZoneInfo zone,
        Medicine medicine,
        IEnumerable<MedicationScheduleHistory> schedule,
        IReadOnlyList<MedicationAdministrationSlot> slots,
        IEnumerable<MedicationSuspension> suspensions,
        decimal stockAtStartOfDay,
        IEnumerable<MedicationIntake> intakes,
        TimeSpan dueWindow)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(medicine);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(suspensions);
        if (stockAtStartOfDay <= 0m) return [];
        if (day < medicine.StartDate || (medicine.EndDate is { } end && day > end)) return [];
        if (DailyConsumption.RateOn(day, schedule, slots) == 0m) return [];
        if (SuspensionState.IsSuspendedOn(day, suspensions)) return [];

        var records = Records(day, slots, intakes, zone);
        var result = new List<DoseSlotStatus>();
        foreach (var slot in Timed(slots))
        {
            var at = AtLocal(day, slot.Time!.Value, zone);
            if (records.TryGetValue(slot.Id, out var intake))
            {
                result.Add(new DoseSlotStatus(slot, at, DoseSlotState.Recorded, intake));
                continue;
            }
            var state = at > now ? DoseSlotState.Upcoming
                : now - at <= dueWindow ? DoseSlotState.Due
                : DoseSlotState.NotRecorded;
            result.Add(new DoseSlotStatus(slot, at, state, null));
        }
        return result;
    }

    public static DateTimeOffset AtLocal(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = day.ToDateTime(time);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static bool SameMinute(DateTime value, DateOnly day, TimeOnly time)
        => DateOnly.FromDateTime(value) == day && value.Hour == time.Hour && value.Minute == time.Minute;
}

public enum DoseSlotState
{
    // The slot's time is still ahead.
    Upcoming,

    // The time has passed, within the due window, and nothing is recorded.
    Due,

    // The due window has passed and nothing is recorded. It says only
    // that no entry exists, not whether the dose was taken.
    NotRecorded,

    // An intake covers the slot (any status).
    Recorded,
}

public sealed record DoseSlotStatus(
    MedicationAdministrationSlot Slot,
    DateTimeOffset At,
    DoseSlotState State,
    MedicationIntake? Intake);
