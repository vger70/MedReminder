using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Monitoring;

// Generates the missing StockMovement (Kind=Consumption) entries for
// every active medicine, from the day after the last recorded
// consumption up to "today". Respects:
//  - the therapy's StartDate / EndDate (via ConsumptionMaterializer);
//  - suspension periods (idem);
//  - the versioned schedule (idem).
//
// Idempotency:
//  - for a single day: if a consumption for (medicineId, day) already
//    exists, the Application skips the day thanks to
//    GetLastConsumptionDayAsync.
//  - at the persistence level: a unique constraint on
//    (MedicineId, Kind=Consumption, day) provided by the DB schema
//    (Increment 3) guards against concurrent double calls.
public sealed class ConsumptionCatchUp
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ConsumptionCatchUp(
        IMedicineRepository medicines,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IMedicationAdministrationSlotRepository slots,
        IStockMovementRepository stock,
        IMedicationIntakeRepository intakes,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _schedules = schedules;
        _suspensions = suspensions;
        _slots = slots;
        _stock = stock;
        _intakes = intakes;
        _uow = uow;
        _clock = clock;
    }

    // Returns the total number of Consumption movements created.
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var today = LocalToday();
        var medicines = await _medicines.ListActiveAsync(cancellationToken);

        var created = 0;
        foreach (var medicine in medicines)
        {
            var lastDay = await _stock.GetLastConsumptionDayAsync(medicine.Id, cancellationToken);
            var rangeStart = lastDay is null
                ? medicine.StartDate
                : lastDay.Value.AddDays(1);

            // Materialize up to "yesterday" inclusive: today's
            // consumption will be written on the next run, once the
            // day has closed. Avoids double-decrementing today if the
            // user records a manual intake shortly after catch-up.
            var rangeEnd = today.AddDays(-1);
            if (rangeStart > rangeEnd) continue;

            var schedule = await _schedules.ListForMedicineAsync(medicine.Id, cancellationToken);
            var suspensions = await _suspensions.ListForMedicineAsync(medicine.Id, cancellationToken);
            var slots = await _slots.ListForMedicineAsync(medicine.Id, cancellationToken);

            var plan = ConsumptionMaterializer.Plan(
                medicine, rangeStart, rangeEnd, schedule, suspensions, slots);
            if (plan.Count == 0) continue;

            // Days covered by a manual intake record (any status) do
            // NOT generate an automatic consumption: the user has
            // already declared the actual state of the day (Taken →
            // movement created by the RegisterIntake use case;
            // Skipped / Cancelled → no consumption). See spec §6.
            var manualDays = await _intakes.ListManualIntakeDaysAsync(
                medicine.Id, rangeStart, rangeEnd, cancellationToken);
            var manualDaysSet = manualDays.Count == 0
                ? null
                : new HashSet<DateOnly>(manualDays);

            var movements = new List<StockMovement>(plan.Count);
            foreach (var day in plan)
            {
                if (manualDaysSet is not null && manualDaysSet.Contains(day.Day))
                {
                    continue;
                }
                movements.Add(new StockMovement
                {
                    MedicineId = medicine.Id,
                    OccurredAt = ToLocalMiddayOffset(day.Day),
                    Kind = StockMovementKind.Consumption,
                    QuantityDelta = -day.Quantity,
                    StockEpoch = medicine.StockEpoch,
                });
            }

            if (movements.Count == 0) continue;
            await _stock.AddRangeAsync(movements, cancellationToken);
            created += movements.Count;
        }

        if (created > 0)
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        return created;
    }

    private DateOnly LocalToday()
    {
        var now = _clock.GetUtcNow();
        var localTz = _clock.LocalTimeZone;
        var local = TimeZoneInfo.ConvertTime(now, localTz);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private DateTimeOffset ToLocalMiddayOffset(DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(12, 0));
        var offset = _clock.LocalTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
