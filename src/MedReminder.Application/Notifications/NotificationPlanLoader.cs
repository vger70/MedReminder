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
// With the prescription repositories, the prescriptions too, for the
// low-stock action (backlog B3-01) and, with the reminder events, the
// "valid until" reminders (backlog B3-02). With the deadline
// repositories, the administrative deadlines (backlog B3-03).
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
    private readonly IPrescriptionRepository? _prescriptions;
    private readonly IPrescriptionDispensationRepository? _dispensations;
    private readonly IPrescriptionReminderEventRepository? _prescriptionEvents;
    private readonly IDeadlineRepository? _deadlines;
    private readonly IDeadlineReminderEventRepository? _deadlineEvents;
    private readonly IDoseRepetitionPolicy? _repetitions;

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
        IProfileSettingsStore? settings = null,
        IPrescriptionRepository? prescriptions = null,
        IPrescriptionDispensationRepository? dispensations = null,
        IPrescriptionReminderEventRepository? prescriptionEvents = null,
        IDeadlineRepository? deadlines = null,
        IDeadlineReminderEventRepository? deadlineEvents = null,
        IDoseRepetitionPolicy? repetitions = null)
    {
        _repetitions = repetitions;
        _deadlines = deadlines;
        _deadlineEvents = deadlineEvents;
        _prescriptionEvents = prescriptionEvents;
        _prescriptions = prescriptions;
        _dispensations = dispensations;
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
        options = Resolve(options);
        var now = _clock.GetUtcNow();
        var zone = _clock.LocalTimeZone;
        var states = await LoadAsync(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime),
            cancellationToken);
        return NotificationPlanner.Plan(states, now, zone, options, await LoadDeadlinesAsync(cancellationToken));
    }

    // The plan with the device text of each notice
    // (PlannedNotificationTexts), for hosts that hand the notifications to
    // the platform ahead of time. A notice whose text cannot be built is
    // left out.
    public async Task<IReadOnlyList<PlannedNotificationText>> PlanWithTextsAsync(
        NotificationPlanOptions? options, ILocalizationService? localization, CancellationToken cancellationToken)
    {
        options = Resolve(options);
        var now = _clock.GetUtcNow();
        var zone = _clock.LocalTimeZone;
        var states = await LoadAsync(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime),
            cancellationToken);
        var deadlines = await LoadDeadlinesAsync(cancellationToken);
        var plan = NotificationPlanner.Plan(states, now, zone, options, deadlines);

        var byId = states.ToDictionary(s => s.Medicine.Id);
        var names = new Dictionary<Guid, string>();
        foreach (var id in plan.Where(n => n.Kind == PlannedNotificationKind.Deadline)
                     .Select(n => deadlines?.Deadlines.FirstOrDefault(d => d.Id == n.DeadlineId)?.MedicineId)
                     .OfType<Guid>().Distinct())
        {
            if (byId.TryGetValue(id, out var state)) names[id] = state.Medicine.Name;
            else if (await _medicines.GetAsync(id, cancellationToken) is { } medicine) names[id] = medicine.Name;
        }

        var result = new List<PlannedNotificationText>();
        foreach (var notice in plan)
        {
            if (PlannedNotificationTexts.Build(notice, byId, deadlines, names, localization) is { } text)
            {
                result.Add(new PlannedNotificationText(notice, text.Title, text.Body));
            }
        }
        return result;
    }

    // The package lead days of the profile settings, when there are any.
    private NotificationPlanOptions Resolve(NotificationPlanOptions? options)
    {
        options ??= NotificationPlanOptions.Default;
        return _settings is null ? options : options with { PackageLeadDays = PackageSettings.LeadDays(_settings) };
    }

    // Null without the deadline repositories: no deadline is planned.
    public async Task<DeadlinePlanState?> LoadDeadlinesAsync(CancellationToken cancellationToken)
    {
        if (_deadlines is null || _deadlineEvents is null) return null;
        var deadlines = await _deadlines.ListAllAsync(cancellationToken);
        var reminded = new HashSet<DeadlineReminderKey>();
        foreach (var deadline in deadlines)
        {
            if (deadline.DoneOn is null
                && await _deadlineEvents.ExistsAsync(deadline.Id, deadline.DueOn, cancellationToken))
            {
                reminded.Add(new DeadlineReminderKey(deadline.Id, deadline.DueOn));
            }
        }
        return new DeadlinePlanState(deadlines, reminded);
    }

    // Slots an intake of today answers, read only for a medicine with a
    // dose reminder, a timed slot and an intake of today (LoadAsync).
    private async Task<IReadOnlySet<Guid>?> RecordedSlotsAsync(Guid medicineId, DateOnly today,
        IReadOnlyList<Domain.Medicines.MedicationAdministrationSlot> slots, CancellationToken cancellationToken)
    {
        if (DoseSlots.Timed(slots).Count == 0) return null;
        var intakes = await _intakes.ListForDayAsync(medicineId, today, cancellationToken);
        return DoseSlots.Records(today, slots, intakes, _clock.LocalTimeZone).Keys.ToHashSet();
    }

    public async Task<IReadOnlyList<MedicinePlanState>> LoadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var withIntake = new HashSet<Guid>(await _intakes.ListMedicinesWithDayIntakeAsync(today, cancellationToken));
        var booked = new HashSet<Guid>(withIntake);
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
            // The last repetition of each slot shown today (mobile issue #128).
            // Only one already due can have been shown: a slot still ahead
            // reads nothing.
            var repetition = medicine.RemindOnDose ? _repetitions?.For(medicine) : null;
            Dictionary<Guid, int>? repeated = null;
            if (repetition is not null)
            {
                var zone = _clock.LocalTimeZone;
                var localNow = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), zone);
                foreach (var slot in DoseSlots.Timed(slots))
                {
                    var due = repetition.LatestDue(DoseSlots.AtLocal(today, slot.Time!.Value, zone), localNow);
                    for (var k = due; k >= 1; k--)
                    {
                        if (!await _doseEvents.ExistsAsync(medicine.Id, DoseRepetition.SlotKey(slot.Id, k), today,
                                cancellationToken)) continue;
                        (repeated ??= [])[slot.Id] = k;
                        break;
                    }
                }
            }

            // A repeatable prescription changes a low-stock notice; with the
            // reminder events, any prescription may be reminded before it
            // lapses.
            IReadOnlyList<Domain.Prescriptions.Prescription>? prescriptions = null;
            HashSet<PrescriptionReminderKey>? prescriptionsReminded = null;
            if (_prescriptions is not null && _dispensations is not null)
            {
                var all = await _prescriptions.ListForMedicineAsync(medicine.Id, cancellationToken);
                prescriptions = _prescriptionEvents is null ? [.. all.Where(p => p.IsRepeatable)] : all;
                if (_prescriptionEvents is not null)
                {
                    prescriptionsReminded = [];
                    foreach (var p in all)
                    {
                        if (p.ValidUntil is { } until
                            && await _prescriptionEvents.ExistsAsync(p.Id, until, cancellationToken))
                        {
                            prescriptionsReminded.Add(new PrescriptionReminderKey(p.Id, until));
                        }
                    }
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
                shown,
                prescriptions,
                prescriptions is { Count: > 0 } && _dispensations is not null
                    ? await _dispensations.CountByPrescriptionAsync(medicine.Id, cancellationToken)
                    : null,
                prescriptionsReminded,
                medicine.RemindOnDose && withIntake.Contains(medicine.Id)
                    ? await RecordedSlotsAsync(medicine.Id, today, slots, cancellationToken)
                    : null,
                repetition,
                repeated));
        }
        return result;
    }
}
