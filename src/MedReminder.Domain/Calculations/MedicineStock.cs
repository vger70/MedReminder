using MedReminder.Domain.Stock;

namespace MedReminder.Domain.Calculations;

// Calcolo della quantità corrente di una medicina come somma algebrica
// dei suoi movimenti (spec §17). Non memorizzato: sempre ricomputato.
public static class MedicineStock
{
    // Somma di QuantityDelta su tutti i movimenti. Se il totale è
    // negativo (dato inconsistente causato da correzioni manuali eccessive)
    // viene clampato a zero: fisicamente la scorta non può essere negativa
    // e la UI mostrerà comunque il warning tramite la Application layer.
    public static decimal Current(IEnumerable<StockMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(movements);
        var sum = 0m;
        foreach (var m in movements)
        {
            sum += m.QuantityDelta;
        }
        return sum < 0m ? 0m : sum;
    }

    // Sub-total per una specifica epoch di stock. Utile per diagnostica.
    public static decimal CurrentForEpoch(IEnumerable<StockMovement> movements, int epoch)
    {
        ArgumentNullException.ThrowIfNull(movements);
        var sum = 0m;
        foreach (var m in movements)
        {
            if (m.StockEpoch == epoch) sum += m.QuantityDelta;
        }
        return sum;
    }

    // Verifica se applicare un ulteriore delta manderebbe il totale sotto
    // zero. Usato dalla Application per bloccare correzioni manuali che
    // renderebbero il magazzino inconsistente.
    public static bool WouldGoNegative(decimal currentStock, decimal proposedDelta)
        => currentStock + proposedDelta < 0m;
}
