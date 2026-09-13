using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Consumo giornaliero effettivo su una data data, letto dalla schedule
// versionata della medicina (MedicationScheduleHistory).
//
// Regola: si sceglie l'entry con EffectiveFrom <= data più recente.
// Se non esiste alcuna entry applicabile la funzione ritorna 0
// (nessun consumo previsto → nessuna ETA, come da spec §7).
public static class DailyConsumption
{
    public static decimal RateOn(
        DateOnly date,
        IEnumerable<MedicationScheduleHistory> scheduleHistory)
    {
        ArgumentNullException.ThrowIfNull(scheduleHistory);

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
