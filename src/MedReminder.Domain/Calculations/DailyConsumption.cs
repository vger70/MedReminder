using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Effective daily consumption on a given date.
//
// Two models are supported (spec §9, Increment 10):
//   A) Legacy dose × frequency model — used when the medicine has NO
//      administration slots defined. The versioned schedule
//      (MedicationScheduleHistory) yields the dose / frequency
//      applicable to the requested day.
//   B) Slot model — used when the medicine HAS slots defined: daily
//      consumption is the SUM of the slots' doses, independent of the
//      day ("current retroactive" semantics: slot changes also apply
//      to past days that have not been materialized yet — for personal
//      use this makes no practical difference).
//
// If neither path applies, returns 0m (spec §7: no ETA when daily
// consumption cannot be determined).
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
        return latest.DosePerAdministration * latest.AdministrationsPerDay;
    }
}
