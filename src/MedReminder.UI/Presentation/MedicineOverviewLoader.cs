using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;

namespace MedReminder.UI.Presentation;

// Costruisce la lista dei MedicineListItem per la MainForm.
// Riutilizza le funzioni pure del dominio (MedicineStock,
// DailyConsumption, SuspensionState, RunOutForecast): l'aggregato non
// duplica logica, si limita a orchestrare i repository e a mappare la
// vista.
internal sealed class MedicineOverviewLoader
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly TimeProvider _clock;
    private readonly ILocalizationService _loc;

    public MedicineOverviewLoader(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IMedicationAdministrationSlotRepository slots,
        TimeProvider clock,
        ILocalizationService localization)
    {
        _medicines = medicines;
        _stock = stock;
        _schedules = schedules;
        _suspensions = suspensions;
        _slots = slots;
        _clock = clock;
        _loc = localization;
    }

    public async Task<IReadOnlyList<MedicineListItem>> LoadAsync(CancellationToken cancellationToken)
    {
        var today = LocalToday();
        var medicines = await _medicines.ListAllAsync(cancellationToken);
        var items = new List<MedicineListItem>(medicines.Count);

        foreach (var m in medicines)
        {
            var movements = await _stock.ListForMedicineAsync(m.Id, cancellationToken);
            var currentStock = MedicineStock.Current(movements);

            var schedule = await _schedules.ListForMedicineAsync(m.Id, cancellationToken);
            var slots = await _slots.ListForMedicineAsync(m.Id, cancellationToken);
            var rate = DailyConsumption.RateOn(today, schedule, slots);

            var suspensions = await _suspensions.ListForMedicineAsync(m.Id, cancellationToken);
            var isSuspended = SuspensionState.IsSuspendedOn(today, suspensions);

            var forecast = RunOutForecast.Compute(today, currentStock, rate, isSuspended);
            var status = ComputeStatus(m.IsActive, isSuspended, currentStock, forecast.DaysRemaining, m.ThresholdDays);

            items.Add(new MedicineListItem
            {
                Id = m.Id,
                Name = m.Name,
                Unit = m.Unit,
                CurrentStock = currentStock,
                DailyRate = rate,
                DaysRemaining = forecast.DaysRemaining,
                EstimatedRunOutDate = forecast.EstimatedRunOutDate,
                ThresholdDays = m.ThresholdDays,
                IsSuspended = isSuspended,
                Status = status,
                StatusDisplay = LocalizeStatus(status),
            });
        }

        return items;
    }

    private static MedicineRowStatus ComputeStatus(
        bool isActive, bool isSuspended, decimal currentStock,
        int? daysRemaining, int thresholdDays)
    {
        if (!isActive) return MedicineRowStatus.Suspended;
        if (isSuspended) return MedicineRowStatus.Suspended;
        if (currentStock <= 0m) return MedicineRowStatus.Empty;
        if (daysRemaining is int d && d <= thresholdDays) return MedicineRowStatus.Warning;
        return MedicineRowStatus.Ok;
    }

    private string LocalizeStatus(MedicineRowStatus status) => status switch
    {
        MedicineRowStatus.Suspended => _loc.Get("Domain.Medicine.Status.Suspended"),
        MedicineRowStatus.Empty => _loc.Get("Domain.Medicine.Status.Empty"),
        MedicineRowStatus.Warning => _loc.Get("Domain.Medicine.Status.Warning"),
        _ => _loc.Get("Domain.Medicine.Status.Ok"),
    };

    private DateOnly LocalToday()
    {
        var utc = _clock.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(utc, _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
