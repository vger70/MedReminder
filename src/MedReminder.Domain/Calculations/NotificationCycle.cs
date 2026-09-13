using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;

namespace MedReminder.Domain.Calculations;

// Regola pura che decide se la medicina è dentro il ciclo di avviso e
// non è già stata notificata con successo per l'epoch corrente.
// La chiamata a questa funzione NON invia nulla: si limita a rispondere
// alla domanda "devo notificare?" (spec §8).
public static class NotificationCycle
{
    // latestNotificationForMedicine: l'ultimo NotificationEvent registrato
    // per questa medicina (indipendente da epoch e da esito). La funzione
    // decide autonomamente se quell'evento neutralizza il ciclo corrente
    // (evento riuscito nell'epoch corrente = "già notificato").
    public static bool ShouldNotify(
        Medicine medicine,
        int? daysRemaining,
        DateOnly? estimatedRunOutDate,
        NotificationEvent? latestNotificationForMedicine)
    {
        ArgumentNullException.ThrowIfNull(medicine);

        if (!medicine.IsActive) return false;
        if (medicine.NotificationChannels == NotificationChannels.None) return false;
        if (daysRemaining is null) return false;
        if (daysRemaining > medicine.ThresholdDays) return false;

        // Se la terapia termina prima dell'esaurimento previsto, nessun
        // avviso: non serve una nuova prescrizione (spec Q2 in ANALYSIS §1.3).
        if (estimatedRunOutDate is not null
            && medicine.EndDate is not null
            && estimatedRunOutDate > medicine.EndDate)
        {
            return false;
        }

        // Un evento riuscito per l'epoch corrente sopprime il re-invio.
        // Un evento fallito NON blocca: la Application/Infrastructure gestisce
        // il retry con back-off; qui il dominio si limita a dire "ancora da
        // notificare". Un evento riuscito su un epoch precedente (rifornimento
        // avvenuto nel frattempo) non blocca: il ciclo riparte con l'epoch.
        if (latestNotificationForMedicine is { } evt
            && evt.StockEpoch == medicine.StockEpoch
            && evt.Success)
        {
            return false;
        }

        return true;
    }
}
