using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.DoseTimes;
using MedReminder.Application.Packages;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Stock;

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
    private readonly DueToday? _dueToday;
    private readonly IStockPackageRepository? _packages;
    private readonly IProfileSettingsStore? _settings;
    private readonly IEquivalenceListStore? _equivalents;

    public MedicineOverviewLoader(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IMedicationAdministrationSlotRepository slots,
        TimeProvider clock,
        ILocalizationService localization,
        IShortageListStore? shortages = null,
        DueToday? dueToday = null,
        IStockPackageRepository? packages = null,
        IProfileSettingsStore? settings = null,
        IEquivalenceListStore? equivalents = null)
    {
        _equivalents = equivalents;
        _shortages = shortages;
        _dueToday = dueToday;
        _packages = packages;
        _settings = settings;
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
        // Read only for a listed AIC in shortage, off the calling thread:
        // the first read after start-up parses the whole file.
        EquivalenceList? equivalents = null;
        var equivalentsRead = false;
        var dueContext = _dueToday is null ? null : await _dueToday.LoadAsync(cancellationToken);
        // Read once, and only when some medicine has packages.
        PackageLeadDays? leadDays = null;

        foreach (var m in medicines)
        {
            var movements = await _stock.ListForMedicineAsync(m.Id, cancellationToken);
            var ledgerStock = MedicineStock.Current(movements);

            var schedule = await _schedules.ListForMedicineAsync(m.Id, cancellationToken);
            var slots = await _slots.ListForMedicineAsync(m.Id, cancellationToken);
            var suspensions = await _suspensions.ListForMedicineAsync(m.Id, cancellationToken);

            // The list shows the stock after today's doses already due
            // (ANALYSIS-INTRADAY-CONSUMPTION.md §4); the forecast keeps
            // the start-of-day stock, which today's whole plan still
            // starts from, so it does not count those doses twice.
            var due = dueContext is null ? 0m : DueToday.Compute(dueContext, m, schedule, suspensions, slots);
            var currentStock = Math.Max(0m, ledgerStock - due);

            // Shared with the therapy timeline so both views show the
            // same run-out date.
            var result = MedicineForecast.Compute(today, ledgerStock, schedule, slots, suspensions);
            var rate = result.DailyRate;
            var isSuspended = result.IsSuspendedToday;
            var forecast = result.RunOut;
            // The run-out date counts today's whole plan from the
            // start-of-day stock, so it does not move when a dose time
            // passes. The days left count from the stock shown: once
            // today's doses are due, the days fully covered after them
            // (101 tablets, 1 a day, after the 07:30 dose: 100 days, run
            // out on day 101 as before).
            var daysRemaining = forecast.DaysRemaining is not null && due > 0m && rate > 0m
                ? (int)decimal.Floor(currentStock / rate)
                : forecast.DaysRemaining;
            var status = ComputeStatus(m.IsActive, isSuspended, currentStock, daysRemaining, m.ThresholdDays);

            items.Add(new MedicineListItem
            {
                Id = m.Id,
                Name = m.Name,
                NationalCode = m.NationalCode,
                Unit = m.Unit,
                CurrentStock = currentStock,
                LedgerStock = ledgerStock,
                DueTodaySoFar = ledgerStock - currentStock,
                DailyRate = rate,
                DailyRateDisplay = rate <= 0m ? "—" : $"{rate:0.##}/{_loc.Get("Ui.MainForm.Column.DailyRate.Unit")}",
                DaysRemaining = daysRemaining,
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
                // The equivalents of a package in shortage
                // (ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK §2.6, U2), under the
                // AIC rule of the menu that opens them.
                if (_equivalents is not null && ItalianPharmacode.NormalizeAic(m.NationalCode) is { } aic)
                {
                    if (!equivalentsRead)
                    {
                        var store = _equivalents;
                        equivalents = await Task.Run(() => store.Load(), cancellationToken);
                        equivalentsRead = true;
                    }
                    if (equivalents?.FindGroup(aic) is not null)
                    {
                        items[^1].SupplyDetail += Environment.NewLine + _loc.Get("Shortage.Detail.SeeEquivalents");
                    }
                }
            }
            if (_packages is not null
                && await _packages.ListForMedicineAsync(m.Id, cancellationToken) is { Count: > 0 } stored)
            {
                // The ledger stock, as the package list and the expiry
                // notices use, so the three agree on the used-up packages.
                leadDays ??= PackageSettings.LeadDays(_settings);
                var packages = PackageListQuery.Build(stored, ledgerStock, today, leadDays);
                if (PackageListQuery.NextExpiring(packages) is { EffectiveExpiry: { } expiry } next)
                {
                    items[^1].NextExpiry = expiry;
                    items[^1].NextExpiryStatus = next.Status;
                    items[^1].ExpiryDisplay = ExpiryText(expiry, next.Status);
                }
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

    // The status is told in words as well as colour.
    private string ExpiryText(DateOnly expiry, PackageExpiryStatus status)
    {
        var date = expiry.ToString("d", _loc.CurrentCulture);
        return status switch
        {
            PackageExpiryStatus.Expired => _loc.Get("Packages.Expiry.Expired", date),
            PackageExpiryStatus.ExpiringSoon => _loc.Get("Packages.Expiry.Soon", date),
            _ => date,
        };
    }

    private DateOnly LocalToday()
    {
        var utc = _clock.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(utc, _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
