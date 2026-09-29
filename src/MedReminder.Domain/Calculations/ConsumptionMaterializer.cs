using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Represents a computed daily consumption: the Application translates
// this structure into a Consumption StockMovement with
// QuantityDelta = -Quantity when persisting.
public sealed record MaterializedConsumption(DateOnly Day, decimal Quantity);

// Plans automatic consumptions between two dates, applying:
//  - the therapy time window (StartDate / EndDate);
//  - suspension periods (skips suspended days);
//  - the versioned schedule (dose × administrations may change over
//    time).
//
// Idempotency for (MedicineId, day, Consumption) is the persistence
// layer's responsibility (unique constraint on a composite index).
// Here we only propose the correct list.
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

        // Materialize the collections once: they are iterated many
        // times inside the per-day loop.
        var scheduleList = scheduleHistory.ToList();
        var suspensionList = suspensions.ToList();
        var slotsList = administrationSlots?.ToList();

        // Do not go past the therapy end date.
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
