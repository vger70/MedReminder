using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Run-out estimate by walking the planned quantities of the following
// days until the stock falls short (issue #313), so a taper, a cyclic
// pause, a weekly gap or a suspension ahead count for what they plan.
//
// What each rule leaves to the caller or keeps:
//  - The therapy end date is not applied: the date may fall after it.
//    NotificationCycle drops such a warning and the timeline flags it.
//  - A suspension in progress gives no estimate and so no warning.
//  - The date is the first day the stock cannot cover: on a pause with
//    no stock left, the next day with a dose.
//  - No day falling short and nothing planned any more (as needed, a
//    bounded course, an open suspension ahead): no estimate.
//  - Past the horizon the next year of the plan gives the average, which
//    keeps floor(stock / rate) on a fixed regimen.
public static class RunOutSimulation
{
    public const int HorizonDays = 366;

    // todayBooked: the ledger already holds today's consumption (an intake
    // or a count that materialized it), so the walk starts tomorrow. The
    // date then stays where it was and the days count those covered after
    // today, as the main list does once today's doses are due.
    public static RunOutForecastResult Compute(
        Medicine medicine,
        DateOnly today,
        decimal currentStock,
        bool todayBooked,
        IEnumerable<MedicationScheduleHistory> scheduleHistory,
        IEnumerable<MedicationAdministrationSlot>? administrationSlots,
        IEnumerable<MedicationSuspension> suspensions)
    {
        ArgumentNullException.ThrowIfNull(suspensions);
        var suspensionList = suspensions as IReadOnlyList<MedicationSuspension> ?? suspensions.ToList();
        if (SuspensionState.IsSuspendedOn(today, suspensionList)) return new RunOutForecastResult(null, null);

        return Walk(medicine, todayBooked ? today.AddDays(1) : today, currentStock,
            scheduleHistory, administrationSlots, suspensionList);
    }

    // The whole days `stock` covers from `first` on.
    public static int? DaysCoveredFrom(
        Medicine medicine,
        DateOnly first,
        decimal stock,
        IEnumerable<MedicationScheduleHistory> scheduleHistory,
        IEnumerable<MedicationAdministrationSlot>? administrationSlots,
        IEnumerable<MedicationSuspension> suspensions)
        => Walk(medicine, first, stock, scheduleHistory, administrationSlots, suspensions).DaysRemaining;

    private static RunOutForecastResult Walk(
        Medicine medicine,
        DateOnly first,
        decimal stock,
        IEnumerable<MedicationScheduleHistory> scheduleHistory,
        IEnumerable<MedicationAdministrationSlot>? administrationSlots,
        IEnumerable<MedicationSuspension> suspensions)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        ArgumentNullException.ThrowIfNull(scheduleHistory);
        ArgumentNullException.ThrowIfNull(suspensions);
        var schedule = scheduleHistory as IReadOnlyList<MedicationScheduleHistory> ?? scheduleHistory.ToList();
        var slots = administrationSlots as IReadOnlyList<MedicationAdministrationSlot> ?? administrationSlots?.ToList();
        var suspensionList = suspensions as IReadOnlyList<MedicationSuspension> ?? suspensions.ToList();

        var rest = Math.Max(stock, 0m);
        var horizon = first.AddDays(HorizonDays);
        foreach (var planned in ConsumptionMaterializer.PlanPastTherapyEnd(medicine, first, horizon, schedule, suspensionList, slots))
        {
            if (rest < planned.Quantity)
            {
                return new RunOutForecastResult(planned.Day.DayNumber - first.DayNumber, planned.Day);
            }
            rest -= planned.Quantity;
        }

        var nextYear = ConsumptionMaterializer
            .PlanPastTherapyEnd(medicine, horizon.AddDays(1), horizon.AddDays(HorizonDays), schedule, suspensionList, slots)
            .Sum(p => p.Quantity);
        if (nextYear <= 0m) return new RunOutForecastResult(null, null);

        var maxDays = DateOnly.MaxValue.DayNumber - first.DayNumber;
        var days = HorizonDays + 1 + rest * HorizonDays / nextYear;
        var whole = days >= maxDays ? maxDays : (int)decimal.Floor(days);
        return new RunOutForecastResult(whole, first.AddDays(whole));
    }
}
