using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Effective daily consumption on a given date.
//
// Three models compose (spec §9, Increment 10, A1):
//   A) Slot model — takes precedence when the medicine HAS slots
//      defined: daily consumption is the SUM of the doses of the slots
//      that are not as-needed, independent of the day ("current
//      retroactive" semantics). When every slot is as-needed the rate
//      is 0: the medicine behaves as PRN (docs/analysis/
//      ANALYSIS-INTRADAY-CONSUMPTION.md §5.1).
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
                if (!slot.IsAsNeeded) slotSum += slot.Dose;
                slotCount++;
            }
            if (slotCount > 0)
            {
                return slotSum;
            }
        }

        if (RowInForce(date, scheduleHistory) is not { } latest) return 0m;

        var schedule = ScheduleCodec.Deserialize(
            latest.ScheduleKind,
            latest.SchedulePayload,
            latest.DosePerAdministration,
            latest.AdministrationsPerDay);
        return schedule.RateOn(date, latest.EffectiveFrom);
    }

    // The schedule row in force on `date`: the latest EffectiveFrom on or
    // before it. '>=': of two rows with the same EffectiveFrom the later
    // one in recording order wins, as in LedgerDeriver
    // (ANALYSIS-B1-MOBILE-SYNC.md §17). Callers pass rows in recording
    // order within a date.
    public static MedicationScheduleHistory? RowInForce(
        DateOnly date, IEnumerable<MedicationScheduleHistory> scheduleHistory)
    {
        ArgumentNullException.ThrowIfNull(scheduleHistory);
        MedicationScheduleHistory? latest = null;
        foreach (var entry in scheduleHistory)
        {
            if (entry.EffectiveFrom > date) continue;
            if (latest is null || entry.EffectiveFrom >= latest.EffectiveFrom)
            {
                latest = entry;
            }
        }
        return latest;
    }
}
