using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Effective daily consumption on a given date.
//
// Three models compose (spec §9, Increment 10, A1):
//   A) Slot model — takes precedence when the medicine HAS slots
//      defined: daily consumption is the SUM of the slots' doses,
//      independent of the day ("current retroactive" semantics).
//   B) Schedule model (A1) — used when the medicine has no slots and
//      the applicable MedicationScheduleHistory entry carries a
//      non-FixedDaily ScheduleKind. Dispatches via ScheduleCodec to
//      the Schedule value object's RateOn.
//   C) Legacy dose × frequency model — the FixedDaily default; falls
//      out of the Schedule dispatch through the codec, so
//      Deserialize(FixedDaily, null, dose, admin).RateOn(day, anchor)
//      returns dose × admin as before.
//
// If none of these paths yields a positive rate, returns 0m (spec §7:
// no ETA when daily consumption cannot be determined).
public static class DailyConsumption
{
    public static decimal RateOn(
        DateOnly date,
        IEnumerable<MedicationScheduleHistory> scheduleHistory)
    {
        return RateOn(date, scheduleHistory, administrationSlots: null);
    }

    public static decimal RateOn(
        DateOnly date,
        IEnumerable<MedicationScheduleHistory> scheduleHistory,
        IEnumerable<MedicationAdministrationSlot>? administrationSlots)
    {
        ArgumentNullException.ThrowIfNull(scheduleHistory);

        if (administrationSlots is not null)
        {
            var slotSum = 0m;
            var slotCount = 0;
            foreach (var slot in administrationSlots)
            {
                slotSum += slot.Dose;
                slotCount++;
            }
            if (slotCount > 0)
            {
                return slotSum;
            }
        }

        MedicationScheduleHistory? latest = null;
        foreach (var entry in scheduleHistory)
        {
            if (entry.EffectiveFrom > date) continue;
            if (latest is null || entry.EffectiveFrom > latest.EffectiveFrom)
            {
                latest = entry;
            }
        }

        if (latest is null) return 0m;

        var schedule = ScheduleCodec.Deserialize(
            latest.ScheduleKind,
            latest.SchedulePayload,
            latest.DosePerAdministration,
            latest.AdministrationsPerDay);
        return schedule.RateOn(date, latest.EffectiveFrom);
    }
}
