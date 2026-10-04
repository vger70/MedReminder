using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Calculations;

namespace MedReminder.Application.Coverage;

// Read-only query behind Therapy → Plan supply: gathers the same data as
// the main table for every active medicine and hands it to the pure
// CoveragePlanner. Writes nothing.
public sealed class CoveragePlanQuery
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly TimeProvider _clock;

    public CoveragePlanQuery(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IMedicationAdministrationSlotRepository slots,
        TimeProvider clock)
    {
        _medicines = medicines;
        _stock = stock;
        _schedules = schedules;
        _suspensions = suspensions;
        _slots = slots;
        _clock = clock;
    }

    public DateOnly LocalToday()
    {
        var local = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public async Task<CoveragePlan> LoadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var today = LocalToday();
        var medicines = await _medicines.ListActiveAsync(cancellationToken);
        var inputs = new List<CoverageInput>(medicines.Count);
        foreach (var m in medicines)
        {
            var movements = await _stock.ListForMedicineAsync(m.Id, cancellationToken);
            var schedule = await _schedules.ListForMedicineAsync(m.Id, cancellationToken);
            var suspensions = await _suspensions.ListForMedicineAsync(m.Id, cancellationToken);
            var slots = await _slots.ListForMedicineAsync(m.Id, cancellationToken);
            inputs.Add(new CoverageInput(m, schedule, suspensions, slots,
                MedicineStock.Current(movements), RestockByScanQuery.LastNewPackageQuantity(movements)));
        }
        return CoveragePlanner.Build(inputs, today, from, to);
    }
}
