namespace MedReminder.Domain.Calculations;

// Result of the run-out forecast. DaysRemaining and EstimatedRunOutDate
// are null when the forecast cannot be determined (spec §7: no daily
// consumption, suspended medicine, etc.).
public sealed record RunOutForecastResult(
    int? DaysRemaining,
    DateOnly? EstimatedRunOutDate);

public static class RunOutForecast
{
    // Computes days remaining and run-out ETA from:
    //  - today: logical reference date (supplied by the caller, usually
    //    TimeProvider.GetLocalNow().ToDateOnly()) — avoids the
    //    dependency on DateTime.Now required by spec §24.
    //  - currentStock: current quantity (already computed by
    //    MedicineStock).
    //  - dailyRate: effective daily consumption on that date
    //    (DailyConsumption).
    //  - isSuspendedToday: true if a suspension covers `today`.
    //
    // Rules:
    //  - Suspended today           → (null, null).
    //  - dailyRate <= 0            → (null, null).
    //  - currentStock <= 0         → (0, today) (already depleted).
    //  - otherwise                 → floor(stock / rate) days; ETA in
    //                                the future.
    public static RunOutForecastResult Compute(
        DateOnly today,
        decimal currentStock,
        decimal dailyRate,
        bool isSuspendedToday)
    {
        if (isSuspendedToday) return new RunOutForecastResult(null, null);
        if (dailyRate <= 0m) return new RunOutForecastResult(null, null);
        if (currentStock <= 0m) return new RunOutForecastResult(0, today);

        var daysDecimal = currentStock / dailyRate;
        var days = (int)decimal.Floor(daysDecimal);
        var eta = today.AddDays(days);
        return new RunOutForecastResult(days, eta);
    }
}
