using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Today's daily rate, suspension state and run-out forecast of one
// medicine, as shown in the main table.
public sealed record MedicineForecastResult(
    decimal DailyRate,
    bool IsSuspendedToday,
    RunOutForecastResult RunOut);

// Composes DailyConsumption, SuspensionState and RunOutSimulation in the
// one way the app uses them. The main table, the therapy timeline, the
// calendar and the low-stock warnings all call this, so the run-out date
// they show cannot diverge.
public static class MedicineForecast
{
    public static MedicineForecastResult Compute(
        Medicine medicine,
        DateOnly today,
        decimal currentStock,
        bool todayBooked,
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

        var rate = DailyConsumption.RateOn(today, schedule, slots);
        var isSuspended = SuspensionState.IsSuspendedOn(today, suspensionList);
        var runOut = RunOutSimulation.Compute(medicine, today, currentStock, todayBooked, schedule, slots, suspensionList);
        return new MedicineForecastResult(rate, isSuspended, runOut);
    }
}
