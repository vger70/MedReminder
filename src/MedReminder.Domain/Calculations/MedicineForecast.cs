using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Today's daily rate, suspension state and run-out forecast of one
// medicine, as shown in the main table.
public sealed record MedicineForecastResult(
    decimal DailyRate,
    bool IsSuspendedToday,
    RunOutForecastResult RunOut);

// Composes DailyConsumption, SuspensionState and RunOutForecast in the
// one way the app uses them. The main table and the therapy timeline
// both call this, so the run-out date they show cannot diverge.
public static class MedicineForecast
{
    public static MedicineForecastResult Compute(
        DateOnly today,
        decimal currentStock,
        IEnumerable<MedicationScheduleHistory> scheduleHistory,
        IEnumerable<MedicationAdministrationSlot>? administrationSlots,
        IEnumerable<MedicationSuspension> suspensions)
    {
        ArgumentNullException.ThrowIfNull(scheduleHistory);
        ArgumentNullException.ThrowIfNull(suspensions);

        var rate = DailyConsumption.RateOn(today, scheduleHistory, administrationSlots);
        var isSuspended = SuspensionState.IsSuspendedOn(today, suspensions);
        var runOut = RunOutForecast.Compute(today, currentStock, rate, isSuspended);
        return new MedicineForecastResult(rate, isSuspended, runOut);
    }
}
