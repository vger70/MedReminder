using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;

namespace MedReminder.Application.Timeline;

// Read-only query behind the therapy timeline view. Gathers the same
// data as the main table (medicines, stock movements, schedule
// history, suspensions, administration slots) and hands it to the
// pure TherapyTimelineBuilder. Writes nothing.
public sealed class TherapyTimelineQuery
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly TimeProvider _clock;

    public TherapyTimelineQuery(
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

    // window == null → the default range around today.
    public async Task<TherapyTimeline> LoadAsync(
        TimelineWindow? window,
        CancellationToken cancellationToken)
    {
        var today = LocalToday();
        var effectiveWindow = window ?? TimelineWindow.Around(today);

        var medicines = await _medicines.ListAllAsync(cancellationToken);
        var inputs = new List<TherapyTimelineInput>(medicines.Count);
        foreach (var m in medicines)
        {
            var movements = await _stock.ListForMedicineAsync(m.Id, cancellationToken);
            var schedule = await _schedules.ListForMedicineAsync(m.Id, cancellationToken);
            var suspensions = await _suspensions.ListForMedicineAsync(m.Id, cancellationToken);
            var slots = await _slots.ListForMedicineAsync(m.Id, cancellationToken);
            inputs.Add(new TherapyTimelineInput(
                m, schedule, suspensions, slots, MedicineStock.Current(movements)));
        }

        return TherapyTimelineBuilder.Build(inputs, today, effectiveWindow);
    }
}
