using MedReminder.Domain.Stock;

namespace MedReminder.Domain.Calculations;

// Computes the current quantity of a medicine as the algebraic sum of
// its movements (spec §17). Never stored: always recomputed.
public static class MedicineStock
{
    // Sum of QuantityDelta across every movement. If the total is
    // negative (inconsistent data caused by excessive manual
    // corrections), it is clamped to zero: physically, stock cannot be
    // negative, and the UI still shows the warning through the
    // Application layer.
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

    // Sub-total for a specific stock epoch. Useful for diagnostics.
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

    // Checks whether applying an additional delta would push the total
    // below zero. Used by the Application to block manual corrections
    // that would leave the stock inconsistent.
    public static bool WouldGoNegative(decimal currentStock, decimal proposedDelta)
        => currentStock + proposedDelta < 0m;
}
