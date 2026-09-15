using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Consumo giornaliero effettivo su una data data.
//
// Due modelli supportati (spec §9 Incremento 10):
//   A) Modello legacy dose×frequenza — usato quando la medicina NON
//      ha slot di somministrazione definiti. La schedule versionata
//      (MedicationScheduleHistory) fornisce dose/frequenza applicabili
//      al giorno richiesto.
//   B) Modello a slot — usato quando la medicina HA slot definiti:
//      il consumo giornaliero è la SOMMA delle dosi degli slot,
//      indipendente dal giorno (semantica "attuale retroattiva":
//      i cambi di slot valgono anche per il passato non ancora
//      materializzato — nel personal-use non fa differenza pratica).
//
// Se nessuna delle due strade è applicabile ritorna 0m (spec §7: nessuna
// ETA quando il consumo giornaliero non è determinabile).
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
