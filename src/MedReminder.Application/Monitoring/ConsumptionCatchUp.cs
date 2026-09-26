using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Monitoring;

// Generates the missing StockMovement (Kind=Consumption) entries for
// every active medicine, up to "yesterday". Respects:
//  - the therapy's StartDate / EndDate (via ConsumptionMaterializer);
//  - suspension periods (idem);
//  - the versioned schedule (idem).
//
// Range: from the day after the last AUTOMATIC consumption (or from
// StartDate when there is none) up to yesterday. A consumption day is
// automatic when no MedicationIntake exists for it: only this class
// and RegisterIntake write Consumption movements, and RegisterIntake
// always writes an intake alongside. Manual intakes therefore do not
// move the start of the range: an intake recorded for today before
// the catch-up has run no longer hides the earlier unmaterialized
// days.
//
// Idempotency:
//  - inside the range, a day that already has a Consumption movement
//    or an intake record (any status) is skipped, so re-running the
//    catch-up never writes a day twice;
//  - against concurrent calls (hosted-service tick and "Check now"):
//    RunAsync holds MonitoringGate, so a second call only reads the
//    ledger after the first one has committed. There is no unique
//    constraint in the schema (see MonitoringGate).
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
    public Task<int> RunAsync(CancellationToken cancellationToken)
        => MonitoringGate.RunExclusiveAsync(RunCoreAsync, cancellationToken);

    private async Task<int> RunCoreAsync(CancellationToken cancellationToken)
    {
        // Materialize up to "yesterday" inclusive: today's consumption
        // will be written on the next run, once the day has closed.
        // Avoids double-decrementing today if the user records a manual
        // intake shortly after catch-up.
        var rangeEnd = LocalToday().AddDays(-1);
        var medicines = await _medicines.ListActiveAsync(cancellationToken);

        var created = 0;
        foreach (var medicine in medicines)
        {
            var ledger = await _stock.ListForMedicineAsync(medicine.Id, cancellationToken);
            var movements = await PlanMissingAsync(medicine, ledger, rangeEnd, cancellationToken);
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

    // Builds, without persisting them, the automatic Consumption
    // movements missing from `ledger` for one medicine, up to rangeEnd
    // inclusive. Shared with ReconcileStock, which needs the stock with
    // consumption materialized up to the moment of a physical count.
    // Callers that persist the result must hold MonitoringGate.
    internal async Task<IReadOnlyList<StockMovement>> PlanMissingAsync(
        Medicine medicine,
        IReadOnlyList<StockMovement> ledger,
        DateOnly rangeEnd,
        CancellationToken cancellationToken)
    {
        if (medicine.StartDate > rangeEnd) return [];

        // Days covered by a manual intake record (any status) do
        // NOT generate an automatic consumption: the user has
        // already declared the actual state of the day (Taken →
        // movement created by the RegisterIntake use case;
        // Skipped / Cancelled → no consumption). See spec §6.
        var intakeDays = new HashSet<DateOnly>(await _intakes.ListManualIntakeDaysAsync(
            medicine.Id, DateOnly.MinValue, DateOnly.MaxValue, cancellationToken));

        var consumptionDays = new HashSet<DateOnly>(ledger
            .Where(m => m.Kind == StockMovementKind.Consumption)
            .Select(m => LocalDay(m.OccurredAt)));

        DateOnly? lastAutomaticDay = null;
        foreach (var day in consumptionDays)
        {
            if (intakeDays.Contains(day)) continue;
            if (lastAutomaticDay is null || day > lastAutomaticDay) lastAutomaticDay = day;
        }

        var rangeStart = lastAutomaticDay is null
            ? medicine.StartDate
            : lastAutomaticDay.Value.AddDays(1);
        if (rangeStart > rangeEnd) return [];

        var schedule = await _schedules.ListForMedicineAsync(medicine.Id, cancellationToken);
        var suspensions = await _suspensions.ListForMedicineAsync(medicine.Id, cancellationToken);
        var slots = await _slots.ListForMedicineAsync(medicine.Id, cancellationToken);

        var plan = ConsumptionMaterializer.Plan(
            medicine, rangeStart, rangeEnd, schedule, suspensions, slots);

        var movements = new List<StockMovement>(plan.Count);
        foreach (var day in plan)
        {
            if (intakeDays.Contains(day.Day) || consumptionDays.Contains(day.Day))
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
        return movements;
    }

    internal DateOnly LocalToday()
    {
        var now = _clock.GetUtcNow();
        var localTz = _clock.LocalTimeZone;
        var local = TimeZoneInfo.ConvertTime(now, localTz);
        return DateOnly.FromDateTime(local.DateTime);
    }

    // Movements read back from SQLite carry a zero offset (UTC ticks);
    // convert to the local zone before taking the calendar day.
    private DateOnly LocalDay(DateTimeOffset occurredAt)
    {
        var local = TimeZoneInfo.ConvertTime(occurredAt, _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private DateTimeOffset ToLocalMiddayOffset(DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(12, 0));
        var offset = _clock.LocalTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
