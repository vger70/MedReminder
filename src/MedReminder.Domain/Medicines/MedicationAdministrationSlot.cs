namespace MedReminder.Domain.Medicines;

// Slot di somministrazione: descrive UNA singola presa giornaliera di
// una medicina — con dose, orario opzionale, e un'indicazione testuale
// che l'utente vede sulla scheda terapia ("al mattino a stomaco vuoto",
// "dopo cena", ecc.).
//
// Modello semplice, per-medicina (non versionato per schedule history):
// quando l'utente cambia i propri orari, sostituisce l'elenco corrente
// di slot. La storia degli orari passati non viene conservata — non
// serve al monitor delle scorte e complicherebbe inutilmente il modello.
//
// Regola di composizione con MedicationScheduleHistory:
//  - se una medicina HA slot, il consumo giornaliero = SUM(slot.Dose).
//  - se non ha slot, si continua a usare la formula legacy
//    Medicine.DosePerAdministration × Medicine.AdministrationsPerDay
//    (Incremento 1..9 comportamento).
// Vedi DailyConsumption.RateOn.
public sealed class MedicationAdministrationSlot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    // Dose della singola presa, nell'unità della medicina.
    public required decimal Dose { get; init; }

    // Orario esatto (fuso locale) se specificato dall'utente; null se
    // l'utente ha preferito indicare solo un'etichetta descrittiva.
    public TimeOnly? Time { get; init; }

    // Descrizione libera del momento della giornata ("al mattino",
    // "prima di dormire", "dopo cena a stomaco pieno", ecc.). Presa da
    // preset o digitata.
    public string? TimingLabel { get; init; }

    // Ordine di visualizzazione: la UI presenta gli slot ordinati per
    // (Time NULLS LAST, Order) così i "senza orario" restano in coda o
    // nell'ordine deciso dall'utente.
    public int Order { get; init; }
}
