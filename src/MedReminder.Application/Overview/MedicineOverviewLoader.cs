using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Calculations;

namespace MedReminder.Application.Overview;

// Builds the list of MedicineListItem for the medicine list (WinForms
// MainForm today, the mobile list later; moved out of MedReminder.UI in
// Phase 1 of docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md).
// Reuses the pure domain functions (MedicineStock, MedicineForecast):
// the aggregate does not duplicate logic, it only orchestrates the
// repositories and maps the view.
public sealed class MedicineOverviewLoader
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly TimeProvider _clock;
    private readonly ILocalizationService _loc;
    private readonly IShortageListStore? _shortages;

    public MedicineOverviewLoader(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IMedicationAdministrationSlotRepository slots,
        TimeProvider clock,
        ILocalizationService localization,
        IShortageListStore? shortages = null)
    {
        _shortages = shortages;
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
        var shortages = _shortages?.Load();

        foreach (var m in medicines)
        {
            var movements = await _stock.ListForMedicineAsync(m.Id, cancellationToken);
            var currentStock = MedicineStock.Current(movements);

            var schedule = await _schedules.ListForMedicineAsync(m.Id, cancellationToken);
            var slots = await _slots.ListForMedicineAsync(m.Id, cancellationToken);
            var suspensions = await _suspensions.ListForMedicineAsync(m.Id, cancellationToken);

            // Shared with the therapy timeline so both views show the
            // same run-out date.
            var result = MedicineForecast.Compute(today, currentStock, schedule, slots, suspensions);
            var rate = result.DailyRate;
            var isSuspended = result.IsSuspendedToday;
            var forecast = result.RunOut;
            var status = ComputeStatus(m.IsActive, isSuspended, currentStock, forecast.DaysRemaining, m.ThresholdDays);

            items.Add(new MedicineListItem
            {
                Id = m.Id,
                Name = m.Name,
                Unit = m.Unit,
                CurrentStock = currentStock,
                DailyRate = rate,
                DailyRateDisplay = rate <= 0m ? "—" : $"{rate:0.##}/{_loc.Get("Ui.MainForm.Column.DailyRate.Unit")}",
                DaysRemaining = forecast.DaysRemaining,
                EstimatedRunOutDate = forecast.EstimatedRunOutDate,
                ThresholdDays = m.ThresholdDays,
                IsSuspended = isSuspended,
                IsActive = m.IsActive,
                Status = status,
                StatusDisplay = LocalizeStatus(status),
            });
            if (shortages?.NoticeFor(m.NationalCode, today) is { } notice)
            {
                items[^1].SupplyDisplay = ShortageTexts.Display(notice, _loc);
                items[^1].SupplyDetail = ShortageTexts.Detail(notice, shortages.ListDate, _loc);
            }
        }

        return items;
    }

    private static MedicineRowStatus ComputeStatus(
        bool isActive, bool isSuspended, decimal currentStock,
        int? daysRemaining, int thresholdDays)
    {
        if (!isActive) return MedicineRowStatus.Inactive;
        if (isSuspended) return MedicineRowStatus.Suspended;
        if (currentStock <= 0m) return MedicineRowStatus.Empty;
        if (daysRemaining is int d && d <= thresholdDays) return MedicineRowStatus.Warning;
        return MedicineRowStatus.Ok;
    }

    private string LocalizeStatus(MedicineRowStatus status) => status switch
    {
        MedicineRowStatus.Inactive => _loc.Get("Domain.Medicine.Status.Inactive"),
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
