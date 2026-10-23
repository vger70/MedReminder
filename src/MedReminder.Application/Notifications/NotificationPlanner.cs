using MedReminder.Application.Packages;
using MedReminder.Application.Prescriptions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Notifications;

public enum PlannedNotificationKind
{
    LowStock,
    DoseReminder,
    PackageExpiry,
    // "Valid until" reminder of an issued prescription (backlog B3-02).
    PrescriptionValidity,
    // Administrative deadline reminder (backlog B3-03).
    Deadline,
}

// One notification the device should show at FireAt. Stage is the
// low-stock stage (NotificationCycle) or the package expiry stage
// (PackageExpiryNoticeEvent); 0 for a dose reminder.
// RepeatablePrescription (low stock only): the repeatable prescription
// with dispensations left on Day, for the text (NotificationTexts) and
// the action (Target). PrescriptionId and ValidUntil ("valid until"
// reminder only): the prescription and the last valid day it is reminded
// for (PrescriptionReminderEvent). DeadlineId and DueOn (deadline
// reminder only): the deadline and the date it is reminded for
// (DeadlineReminderEvent); MedicineId is Guid.Empty for a deadline of the
// profile. DaysRemaining (low stock only): the days of stock the notice
// states. ExpiresOn (package expiry only): the earliest expiry of the
// packages noticed. Both feed the texts (PlannedNotificationTexts).
public sealed record PlannedNotification(
    PlannedNotificationKind Kind,
    Guid MedicineId,
    DateOnly Day,
    DateTimeOffset FireAt,
    int Stage = 0,
    Guid? SlotId = null,
    TimeOnly? SlotTime = null,
    IReadOnlyList<Guid>? PackageIds = null,
    RepeatablePrescriptionNotice? RepeatablePrescription = null,
    Guid? PrescriptionId = null,
    DateOnly? ValidUntil = null,
    Guid? DeadlineId = null,
    DateOnly? DueOn = null,
    int? DaysRemaining = null,
    DateOnly? ExpiresOn = null)
{
    // What the notification is about, for its actions
    // (NotificationActionArguments.ForBody and ButtonsFor), as the desktop
    // toast builds it. A low-stock notice offers to prepare a prescription
    // request, or to open the prescriptions when a repeatable one has
    // dispensations left (backlog B3-01): both only open a screen, neither
    // sends nor records a request. A "valid until" reminder opens the
    // prescriptions (backlog B3-02). A deadline reminder opens the
    // deadlines (backlog B3-03).
    public NotificationTarget Target => Kind switch
    {
        PlannedNotificationKind.DoseReminder => NotificationTarget.DoseReminder(MedicineId, SlotTime!.Value),
        PlannedNotificationKind.LowStock => NotificationTarget.LowStock(MedicineId, RepeatablePrescription is not null),
        PlannedNotificationKind.PrescriptionValidity => NotificationTarget.Prescription(MedicineId),
        PlannedNotificationKind.Deadline => NotificationTarget.Deadline(
            MedicineId == Guid.Empty ? null : MedicineId, DeadlineId),
        _ => NotificationTarget.PackageExpiry(MedicineId),
    };

    // Stable identity across re-plans, so the platform adapter can keep an
    // alarm that is planned again and cancel the ones no longer planned.
    public string Key => Kind switch
    {
        PlannedNotificationKind.DoseReminder => $"dose:{MedicineId:N}:{SlotId:N}:{Day:yyyy-MM-dd}",
        PlannedNotificationKind.LowStock => $"stock:{MedicineId:N}:{Stage}:{Day:yyyy-MM-dd}",
        PlannedNotificationKind.PrescriptionValidity => $"prescription:{PrescriptionId:N}:{ValidUntil:yyyy-MM-dd}",
        PlannedNotificationKind.Deadline => $"deadline:{DeadlineId:N}:{DueOn:yyyy-MM-dd}",
        _ => $"expiry:{MedicineId:N}:{Stage}:{Day:yyyy-MM-dd}",
    };
}

public sealed record NotificationPlanOptions
{
    // Days, today included, over which the daily notices (low stock,
    // package expiry) are planned.
    public int DailyHorizonDays { get; init; } = 14;

    // Rolling window of dose reminders. Bounded so the platform stays far
    // below its alarm limit (Android plan §4.2).
    public TimeSpan DoseWindow { get; init; } = TimeSpan.FromHours(48);

    // Local time of a daily notice due on a later day (B.1 §8.1). A notice
    // due today fires at once, as the desktop's next pass would.
    public TimeOnly DailyNoticeTime { get; init; } = new(9, 0);

    // A slot passed by less than this is still reminded at once
    // (DoseReminderService grace window).
    public TimeSpan DoseGraceWindow { get; init; } = TimeSpan.FromMinutes(30);

    public PackageLeadDays PackageLeadDays { get; init; } = PackageLeadDays.Default;

    public static NotificationPlanOptions Default { get; } = new();
}

// What the planner needs of one medicine, read once per plan
// (NotificationPlanLoader).
//  - CurrentStock: the ledger stock now (MedicineStock.Current).
//  - TodayBooked: today already booked by an intake or a count
//    (DueToday), so the ledger will not add today's automatic consumption.
//  - LatestLowStockEvent: INotificationEventRepository.GetLatestForMedicineAsync.
//  - SlotsRemindedToday: slots with a DoseReminderEvent for today.
//  - PackageNoticesShown: package notices already shown on this device.
//  - Prescriptions: IPrescriptionRepository.ListForMedicineAsync; null
//    for none.
//  - DispensationsCollected: IPrescriptionDispensationRepository
//    .CountByPrescriptionAsync for the medicine; null for none.
//  - PrescriptionsReminded: "valid until" reminders already shown on this
//    device (IPrescriptionReminderEventRepository); null for none.
public sealed record MedicinePlanState(
    Medicine Medicine,
    decimal CurrentStock,
    bool TodayBooked,
    IReadOnlyList<MedicationScheduleHistory> Schedule,
    IReadOnlyList<MedicationAdministrationSlot> Slots,
    IReadOnlyList<MedicationSuspension> Suspensions,
    NotificationEvent? LatestLowStockEvent,
    IReadOnlySet<Guid> SlotsRemindedToday,
    IReadOnlyList<StockPackage> Packages,
    IReadOnlySet<PackageNoticeKey> PackageNoticesShown,
    IReadOnlyList<Prescription>? Prescriptions = null,
    IReadOnlyDictionary<Guid, int>? DispensationsCollected = null,
    IReadOnlySet<PrescriptionReminderKey>? PrescriptionsReminded = null);

public readonly record struct PackageNoticeKey(Guid PackageId, DateOnly EffectiveExpiry, int Stage);

public readonly record struct PrescriptionReminderKey(Guid PrescriptionId, DateOnly ValidUntil);

// What the planner needs of the administrative deadlines of the profile
// (NotificationPlanLoader): every deadline, and the reminders already
// shown on this device (IDeadlineReminderEventRepository).
public sealed record DeadlinePlanState(
    IReadOnlyList<Deadline> Deadlines,
    IReadOnlySet<DeadlineReminderKey> Reminded);

public readonly record struct DeadlineReminderKey(Guid DeadlineId, DateOnly DueOn);

// Notification planner (docs/analysis/ANALYSIS-B1-ANDROID-PLAN.md §4.2,
// ANALYSIS-B1-MOBILE-SYNC.md §8.1, backlog B0-02). The desktop polls
// (MedicationMonitor, DoseReminderService); a phone cannot, so this
// computes, from the same rules, the notifications of the coming days
// that those passes would show on the device. Pure: same state, same
// instant, same zone, same plan.
//
// The stock of a later day is projected as the ledger will book it:
// today's automatic consumption unless today is already booked, then
// the planned quantity of each following day (ConsumptionMaterializer,
// LedgerDeriver rule 2). Each day is then evaluated with the desktop's
// own rules: NotificationCycle for the low-stock stages, the
// DoseReminderService gates for the doses, PackageListQuery.Build for
// the packages. A notice planned for a day counts as shown for the
// following days, as the event the desktop records would.
//
// Only the device notification is planned (the Windows channel of
// NotificationChannels); email stays with the household master
// (ANALYSIS-B1-MOBILE-SYNC.md §8.4). A low-stock notice carries the
// repeatable prescription the desktop toast names (backlog B3-01); the
// "valid until" reminders follow PrescriptionReminders (backlog B3-02),
// the deadline reminders DeadlineReminders (backlog B3-03). Shortages (an
// event of the feed refresh) are not planned here.
public static class NotificationPlanner
{
    private static readonly IReadOnlyDictionary<Guid, int> NoDispensations = new Dictionary<Guid, int>();

    public static IReadOnlyList<PlannedNotification> Plan(
        IEnumerable<MedicinePlanState> medicines,
        DateTimeOffset now,
        TimeZoneInfo zone,
        NotificationPlanOptions? options = null,
        DeadlinePlanState? deadlines = null)
    {
        ArgumentNullException.ThrowIfNull(medicines);
        ArgumentNullException.ThrowIfNull(zone);
        options ??= NotificationPlanOptions.Default;

        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var result = new List<PlannedNotification>();
        // Days of stock to project: the daily horizon, and every day the
        // dose window touches.
        var days = Math.Max(options.DailyHorizonDays, (int)Math.Ceiling(options.DoseWindow.TotalDays) + 2);

        foreach (var state in medicines)
        {
            ArgumentNullException.ThrowIfNull(state);
            if ((state.Medicine.NotificationChannels & NotificationChannels.Windows) == 0) continue;

            var stock = ProjectStock(state, today, days);
            if (state.Medicine.IsActive)
            {
                PlanLowStock(state, stock, today, localNow, zone, options, result);
                if (state.Medicine.RemindOnDose) PlanDoses(state, stock, today, localNow, zone, options, result);
                PlanPrescriptions(state, today, localNow, zone, options, result);
            }
            // Inactive medicines included: their packages stay in the
            // cabinet (PackageExpiryNotices).
            PlanPackages(state, stock, today, localNow, zone, options, result);
        }
        if (deadlines is not null) PlanDeadlines(deadlines, today, localNow, zone, options, result);

        return [.. result.OrderBy(n => n.FireAt).ThenBy(n => n.Key, StringComparer.Ordinal)];
    }

    // Stock at the start of each of `days` days, index 0 = today.
    internal static decimal[] ProjectStock(MedicinePlanState state, DateOnly today, int days)
    {
        var stock = new decimal[Math.Max(days, 1)];
        var raw = state.CurrentStock;
        stock[0] = Clamp(raw);
        for (var i = 1; i < stock.Length; i++)
        {
            var day = today.AddDays(i - 1);
            // An inactive medicine books nothing (LedgerDeriver rule 2,
            // activity history).
            if (state.Medicine.IsActive && (i > 1 || !state.TodayBooked))
            {
                raw -= ConsumptionMaterializer
                    .Plan(state.Medicine, day, day, state.Schedule, state.Suspensions, state.Slots)
                    .Sum(c => c.Quantity);
            }
            stock[i] = Clamp(raw);
        }
        return stock;
    }

    private static void PlanLowStock(MedicinePlanState state, decimal[] stock, DateOnly today,
        DateTimeOffset localNow, TimeZoneInfo zone, NotificationPlanOptions options, List<PlannedNotification> result)
    {
        var medicine = state.Medicine;
        var latest = state.LatestLowStockEvent;
        for (var i = 0; i < options.DailyHorizonDays && i < stock.Length; i++)
        {
            var day = today.AddDays(i);
            var forecast = MedicineForecast.Compute(day, stock[i], state.Schedule, state.Slots, state.Suspensions);
            var runOut = forecast.RunOut;
            if (NotificationCycle.StageToNotify(medicine, runOut.DaysRemaining, runOut.EstimatedRunOutDate, latest)
                is not { } stage)
            {
                continue;
            }

            // The prescription as the desktop pass of that day would see
            // it; a dispensation recorded later re-plans (host).
            var repeatable = state.Prescriptions is { Count: > 0 } prescriptions
                ? RepeatablePrescriptionNotices.Select(prescriptions,
                    state.DispensationsCollected ?? NoDispensations, day)
                : null;
            result.Add(new PlannedNotification(PlannedNotificationKind.LowStock, medicine.Id, day,
                DailyFireAt(day, i, localNow, zone, options), stage, RepeatablePrescription: repeatable,
                DaysRemaining: runOut.DaysRemaining!.Value));
            // The event the desktop records after a shown toast.
            latest = new NotificationEvent
            {
                MedicineId = medicine.Id,
                StockEpoch = medicine.StockEpoch,
                EpochFactId = medicine.StockEpochFactId,
                TriggeredAt = localNow,
                Channel = NotificationChannels.Windows,
                DaysRemainingAtSend = runOut.DaysRemaining!.Value,
                Success = true,
                Stage = stage,
            };
        }
    }

    // DoseReminderService gates, per day: stock above zero, inside the
    // therapy window, a positive rate, not suspended; then each timed slot
    // that is not as-needed.
    private static void PlanDoses(MedicinePlanState state, decimal[] stock, DateOnly today,
        DateTimeOffset localNow, TimeZoneInfo zone, NotificationPlanOptions options, List<PlannedNotification> result)
    {
        var end = localNow + options.DoseWindow;
        var slots = state.Slots.Where(s => s.Time.HasValue && !s.IsAsNeeded).ToList();
        if (slots.Count == 0) return;

        for (var i = 0; ; i++)
        {
            var day = today.AddDays(i);
            if (i >= stock.Length || AtLocal(day, TimeOnly.MinValue, zone) > end) break;
            if (stock[i] <= 0m) continue;
            if (day < state.Medicine.StartDate
                || (state.Medicine.EndDate is { } endDate && day > endDate)) continue;
            if (DailyConsumption.RateOn(day, state.Schedule, state.Slots) == 0m) continue;
            if (SuspensionState.IsSuspendedOn(day, state.Suspensions)) continue;

            foreach (var slot in slots)
            {
                var slotTime = slot.Time!.Value;
                var fireAt = AtLocal(day, slotTime, zone);
                if (fireAt > end) continue;
                if (i == 0)
                {
                    // Missed beyond the grace window, or already reminded.
                    if (localNow - fireAt > options.DoseGraceWindow) continue;
                    if (state.SlotsRemindedToday.Contains(slot.Id)) continue;
                }
                result.Add(new PlannedNotification(PlannedNotificationKind.DoseReminder, state.Medicine.Id, day,
                    fireAt < localNow ? localNow : fireAt, SlotId: slot.Id, SlotTime: slotTime));
            }
        }
    }

    // PackageExpiryNotices for the device channel: per day, the packages
    // needing attention not yet notified; the expired ones when there are
    // any, otherwise those expiring soon.
    private static void PlanPackages(MedicinePlanState state, decimal[] stock, DateOnly today,
        DateTimeOffset localNow, TimeZoneInfo zone, NotificationPlanOptions options, List<PlannedNotification> result)
    {
        if (state.Packages.Count == 0) return;
        var shown = new HashSet<PackageNoticeKey>(state.PackageNoticesShown);
        for (var i = 0; i < options.DailyHorizonDays && i < stock.Length; i++)
        {
            var day = today.AddDays(i);
            var pending = PackageListQuery.Build(state.Packages, stock[i], day, options.PackageLeadDays)
                .Where(item => item.NeedsAttention && item.EffectiveExpiry is not null)
                .Select(item => new PackageNoticeKey(item.Package.Id, item.EffectiveExpiry!.Value,
                    item.Status == PackageExpiryStatus.Expired
                        ? PackageExpiryNoticeEvent.ExpiredStage
                        : PackageExpiryNoticeEvent.SoonStage))
                .Where(key => !shown.Contains(key))
                .ToList();
            if (pending.Count == 0) continue;
            if (pending.Any(k => k.Stage == PackageExpiryNoticeEvent.ExpiredStage))
            {
                pending = [.. pending.Where(k => k.Stage == PackageExpiryNoticeEvent.ExpiredStage)];
            }

            shown.UnionWith(pending);
            result.Add(new PlannedNotification(PlannedNotificationKind.PackageExpiry, state.Medicine.Id, day,
                DailyFireAt(day, i, localNow, zone, options), pending[0].Stage,
                PackageIds: [.. pending.Select(k => k.PackageId)], ExpiresOn: pending.Min(k => k.EffectiveExpiry)));
        }
    }

    // PrescriptionReminders for the device channel: an active medicine's
    // issued prescription not collected yet (a repeatable one with
    // dispensations left), from PrescriptionRules.ReminderLeadDays before
    // "valid until" to the day itself, once per prescription and date. A
    // collection or dispensation recorded later re-plans (host).
    private static void PlanPrescriptions(MedicinePlanState state, DateOnly today, DateTimeOffset localNow,
        TimeZoneInfo zone, NotificationPlanOptions options, List<PlannedNotification> result)
    {
        if (state.Prescriptions is not { Count: > 0 } prescriptions) return;
        var collected = state.DispensationsCollected ?? NoDispensations;
        foreach (var prescription in prescriptions)
        {
            if (prescription.ValidUntil is not { } until) continue;
            if (state.PrescriptionsReminded?.Contains(new PrescriptionReminderKey(prescription.Id, until)) == true)
                continue;
            var count = prescription.IsRepeatable ? collected.GetValueOrDefault(prescription.Id) : 0;
            for (var i = 0; i < options.DailyHorizonDays; i++)
            {
                var day = today.AddDays(i);
                if (!PrescriptionRules.ReminderDue(prescription, day, count)) continue;
                result.Add(new PlannedNotification(PlannedNotificationKind.PrescriptionValidity, state.Medicine.Id,
                    day, DailyFireAt(day, i, localNow, zone, options),
                    PrescriptionId: prescription.Id, ValidUntil: until));
                break;
            }
        }
    }

    // DeadlineReminders for the device channel: a deadline not done, with
    // the device channel, from LeadDays before its date on (overdue days
    // included), once per deadline and date. Not tied to the medicine's
    // channels or activity, as on the desktop. Marking it done, or a
    // changed date, re-plans (host).
    private static void PlanDeadlines(DeadlinePlanState state, DateOnly today, DateTimeOffset localNow,
        TimeZoneInfo zone, NotificationPlanOptions options, List<PlannedNotification> result)
    {
        foreach (var deadline in state.Deadlines)
        {
            ArgumentNullException.ThrowIfNull(deadline);
            if ((deadline.Channels & NotificationChannels.Windows) == 0) continue;
            if (state.Reminded.Contains(new DeadlineReminderKey(deadline.Id, deadline.DueOn))) continue;
            for (var i = 0; i < options.DailyHorizonDays; i++)
            {
                var day = today.AddDays(i);
                if (!DeadlineRules.ReminderDue(deadline, day)) continue;
                result.Add(new PlannedNotification(PlannedNotificationKind.Deadline, deadline.MedicineId ?? Guid.Empty,
                    day, DailyFireAt(day, i, localNow, zone, options), DeadlineId: deadline.Id, DueOn: deadline.DueOn));
                break;
            }
        }
    }

    private static DateTimeOffset DailyFireAt(DateOnly day, int index, DateTimeOffset localNow, TimeZoneInfo zone,
        NotificationPlanOptions options)
        => index == 0 ? localNow : AtLocal(day, options.DailyNoticeTime, zone);

    // The instant of a local wall-clock time, as DoseReminderService
    // builds it: the zone's offset for that local time (a time skipped by
    // a DST change takes the standard offset).
    private static DateTimeOffset AtLocal(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static decimal Clamp(decimal value) => value < 0m ? 0m : value;
}
