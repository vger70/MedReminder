namespace MedReminder.Domain.Calculations;

// Risultato della previsione di esaurimento. DaysRemaining e
// EstimatedRunOutDate sono null quando la previsione non è determinabile
// (spec §7: nessun consumo giornaliero, medicina sospesa, ecc.).
public sealed record RunOutForecastResult(
    int? DaysRemaining,
    DateOnly? EstimatedRunOutDate);

public static class RunOutForecast
{
    // Calcola giorni residui ed ETA di esaurimento a partire da:
    //  - today: data logica di riferimento (fornita dal chiamante,
    //    tipicamente TimeProvider.GetLocalNow().ToDateOnly()) — evita
    //    dipendenza da DateTime.Now come richiesto dalla spec §24.
    //  - currentStock: quantità corrente (già ricalcolata da MedicineStock).
    //  - dailyRate: consumo giornaliero effettivo alla data (DailyConsumption).
    //  - isSuspendedToday: true se una sospensione copre `today`.
    //
    // Regole:
    //  - Sospesa oggi              → (null, null).
    //  - dailyRate <= 0            → (null, null).
    //  - currentStock <= 0         → (0, today) (già esaurita).
    //  - altrimenti                → floor(stock / rate) giorni; ETA in avanti.
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
