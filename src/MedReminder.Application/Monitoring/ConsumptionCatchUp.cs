using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Monitoring;

// Genera i movimenti StockMovement (Kind=Consumption) mancanti per tutte
// le medicine attive, dal giorno successivo all'ultimo consumo già
// registrato fino a "today". Rispetta:
//  - StartDate/EndDate della terapia (via ConsumptionMaterializer);
//  - i periodi di sospensione (idem);
//  - la schedule versionata (idem).
//
// Idempotenza:
//  - alla singola giornata: se un consumo per (medicineId, day) esiste
//    già la Application salta il giorno grazie a GetLastConsumptionDayAsync.
//  - a livello di persistenza: un vincolo unico su
//    (MedicineId, Kind=Consumption, day) offerto dallo schema DB
//    (Incremento 3) protegge da doppie chiamate concorrenti.
public sealed class ConsumptionCatchUp
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ConsumptionCatchUp(
        IMedicineRepository medicines,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IStockMovementRepository stock,
        IMedicationIntakeRepository intakes,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _schedules = schedules;
        _suspensions = suspensions;
        _stock = stock;
        _intakes = intakes;
        _uow = uow;
        _clock = clock;
    }

    // Ritorna il numero totale di movimenti Consumption creati.
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

            // Materializziamo fino a "ieri" incluso: il consumo di oggi
            // sarà scritto al prossimo run, quando la giornata sarà chiusa.
            // Questo evita di scalare due volte oggi se l'utente registra
            // manualmente un'assunzione poco dopo il catch-up.
            var rangeEnd = today.AddDays(-1);
            if (rangeStart > rangeEnd) continue;

            var schedule = await _schedules.ListForMedicineAsync(medicine.Id, cancellationToken);
            var suspensions = await _suspensions.ListForMedicineAsync(medicine.Id, cancellationToken);

            var plan = ConsumptionMaterializer.Plan(
                medicine, rangeStart, rangeEnd, schedule, suspensions);
            if (plan.Count == 0) continue;

            // Le giornate coperte da una registrazione manuale
            // dell'assunzione (qualunque status) NON generano un consumo
            // automatico: l'utente ha già dichiarato lo stato reale della
            // giornata (Taken → movimento creato dal use case RegisterIntake;
            // Skipped/Cancelled → nessun consumo). Vedi spec §6.
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
