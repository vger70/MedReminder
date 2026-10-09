using MedReminder.Application.Abstractions;
using MedReminder.Application.Packages;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Notifications;

// Reads the state NotificationPlanner needs from the profile database
// and plans (backlog B0-02). The ledger must be current: the host runs
// ConsumptionCatchUp first, as the desktop runs it before the monitor.
// Every medicine is read, inactive ones included, for their packages.
public sealed class NotificationPlanLoader
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly INotificationEventRepository _notifications;
    private readonly IDoseReminderEventRepository _doseEvents;
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IStockCountRepository _counts;
    private readonly IStockPackageRepository _packages;
    private readonly IPackageExpiryNoticeEventRepository _packageNotices;
    private readonly TimeProvider _clock;
    private readonly IProfileSettingsStore? _settings;

    public NotificationPlanLoader(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IMedicationAdministrationSlotRepository slots,
        INotificationEventRepository notifications,
        IDoseReminderEventRepository doseEvents,
        IMedicationIntakeRepository intakes,
        IStockCountRepository counts,
        IStockPackageRepository packages,
        IPackageExpiryNoticeEventRepository packageNotices,
        TimeProvider clock,
        IProfileSettingsStore? settings = null)
    {
        _medicines = medicines;
        _stock = stock;
        _schedules = schedules;
        _suspensions = suspensions;
        _slots = slots;
        _notifications = notifications;
        _doseEvents = doseEvents;
        _intakes = intakes;
        _counts = counts;
        _packages = packages;
        _packageNotices = packageNotices;
        _clock = clock;
        _settings = settings;
    }

    public async Task<IReadOnlyList<PlannedNotification>> PlanAsync(
        NotificationPlanOptions? options, CancellationToken cancellationToken)
    {
        options ??= NotificationPlanOptions.Default;
        if (_settings is not null)
        {
            options = options with { PackageLeadDays = PackageSettings.LeadDays(_settings) };
        }
        var now = _clock.GetUtcNow();
        var zone = _clock.LocalTimeZone;
        var states = await LoadAsync(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime),
            cancellationToken);
        return NotificationPlanner.Plan(states, now, zone, options);
    }

    public async Task<IReadOnlyList<MedicinePlanState>> LoadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var booked = new HashSet<Guid>(await _intakes.ListMedicinesWithDayIntakeAsync(today, cancellationToken));
        booked.UnionWith(await _counts.ListMedicinesWithMaterializedCountAsync(today, cancellationToken));

        var result = new List<MedicinePlanState>();
        foreach (var medicine in await _medicines.ListAllAsync(cancellationToken))
        {
            if ((medicine.NotificationChannels & NotificationChannels.Windows) == 0) continue;

            var stock = MedicineStock.Current(await _stock.ListForMedicineAsync(medicine.Id, cancellationToken));
            var slots = await _slots.ListForMedicineAsync(medicine.Id, cancellationToken);
            var reminded = new HashSet<Guid>();
            foreach (var slot in slots)
            {
                if (await _doseEvents.ExistsAsync(medicine.Id, slot.Id.ToString(), today, cancellationToken))
                {
                    reminded.Add(slot.Id);
                }
            }

            var packages = await _packages.ListForMedicineAsync(medicine.Id, cancellationToken);
            var shown = new HashSet<PackageNoticeKey>();
            foreach (var package in packages)
            {
                if (PackageExpiryRules.EffectiveExpiry(package) is not { } expiry) continue;
                foreach (var stage in (int[])[PackageExpiryNoticeEvent.SoonStage, PackageExpiryNoticeEvent.ExpiredStage])
                {
                    if (await _packageNotices.ExistsAsync(package.Id, expiry, stage, NotificationChannels.Windows,
                            cancellationToken))
                    {
                        shown.Add(new PackageNoticeKey(package.Id, expiry, stage));
                    }
                }
            }

            result.Add(new MedicinePlanState(
                medicine,
                stock,
                booked.Contains(medicine.Id),
                await _schedules.ListForMedicineAsync(medicine.Id, cancellationToken),
                slots,
                await _suspensions.ListForMedicineAsync(medicine.Id, cancellationToken),
                await _notifications.GetLatestForMedicineAsync(medicine.Id, cancellationToken),
                reminded,
                packages,
                shown));
        }
        return result;
    }
}
