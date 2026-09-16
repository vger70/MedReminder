using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Rappresenta un consumo giornaliero calcolato: la Application traduce
// questa struttura in uno StockMovement di tipo Consumption con
// QuantityDelta = -Quantity al momento della persistenza.
public sealed record MaterializedConsumption(DateOnly Day, decimal Quantity);

// Pianifica i consumi automatici tra due date, applicando:
//  - la finestra temporale della terapia (StartDate / EndDate);
//  - i periodi di sospensione (salta i giorni sospesi);
//  - la schedule versionata (dose × somministrazioni può variare nel tempo).
//
// L'idempotenza per (MedicineId, giorno, Consumption) è responsabilità
// del layer di persistenza (vincolo unico su indice composito). Qui si
// propone soltanto l'elenco corretto.
public static class ConsumptionMaterializer
{
    public static IReadOnlyList<MaterializedConsumption> Plan(
        Medicine medicine,
        DateOnly rangeStartInclusive,
        DateOnly rangeEndInclusive,
        IEnumerable<MedicationScheduleHistory> scheduleHistory,
        IEnumerable<MedicationSuspension> suspensions)
    {
        return Plan(medicine, rangeStartInclusive, rangeEndInclusive,
            scheduleHistory, suspensions, administrationSlots: null);
    }

    public static IReadOnlyList<MaterializedConsumption> Plan(
        Medicine medicine,
        DateOnly rangeStartInclusive,
        DateOnly rangeEndInclusive,
        IEnumerable<MedicationScheduleHistory> scheduleHistory,
        IEnumerable<MedicationSuspension> suspensions,
        IEnumerable<MedicationAdministrationSlot>? administrationSlots)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        ArgumentNullException.ThrowIfNull(scheduleHistory);
        ArgumentNullException.ThrowIfNull(suspensions);

        if (rangeStartInclusive > rangeEndInclusive)
        {
            return Array.Empty<MaterializedConsumption>();
        }

        // Materializzo le collezioni una sola volta: sono percorse più
        // volte all'interno del ciclo per giorno.
        var scheduleList = scheduleHistory.ToList();
        var suspensionList = suspensions.ToList();
        var slotsList = administrationSlots?.ToList();

        // Non oltre la data di fine terapia.
        var upperBound = rangeEndInclusive;
        if (medicine.EndDate is { } endDate && endDate < upperBound)
        {
            upperBound = endDate;
        }

        var lowerBound = rangeStartInclusive;
        if (medicine.StartDate > lowerBound)
        {
            lowerBound = medicine.StartDate;
        }

        var result = new List<MaterializedConsumption>();
        for (var day = lowerBound; day <= upperBound; day = day.AddDays(1))
        {
            if (SuspensionState.IsSuspendedOn(day, suspensionList)) continue;

            var rate = DailyConsumption.RateOn(day, scheduleList, slotsList);
            if (rate <= 0m) continue;

            result.Add(new MaterializedConsumption(day, rate));
        }
        return result;
    }
}
