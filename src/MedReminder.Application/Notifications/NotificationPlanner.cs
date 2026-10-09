using MedReminder.Application.Packages;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Notifications;

public enum PlannedNotificationKind
{
    LowStock,
    DoseReminder,
    PackageExpiry,
}

// One notification the device should show at FireAt. Stage is the
// low-stock stage (NotificationCycle) or the package expiry stage
// (PackageExpiryNoticeEvent); 0 for a dose reminder.
public sealed record PlannedNotification(
    PlannedNotificationKind Kind,
    Guid MedicineId,
    DateOnly Day,
    DateTimeOffset FireAt,
    int Stage = 0,
    Guid? SlotId = null,
    TimeOnly? SlotTime = null,
    IReadOnlyList<Guid>? PackageIds = null)
{
    // Stable identity across re-plans, so the platform adapter can keep an
    // alarm that is planned again and cancel the ones no longer planned.
    public string Key => Kind switch
    {
        PlannedNotificationKind.DoseReminder => $"dose:{MedicineId:N}:{SlotId:N}:{Day:yyyy-MM-dd}",
        PlannedNotificationKind.LowStock => $"stock:{MedicineId:N}:{Stage}:{Day:yyyy-MM-dd}",
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
    IReadOnlySet<PackageNoticeKey> PackageNoticesShown);

public readonly record struct PackageNoticeKey(Guid PackageId, DateOnly EffectiveExpiry, int Stage);

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
// (ANALYSIS-B1-MOBILE-SYNC.md §8.4). Prescriptions and deadlines (premium,
// M2) and shortages (an event of the feed refresh) are not planned here.
public static class NotificationPlanner
{
    public static IReadOnlyList<PlannedNotification> Plan(
        IEnumerable<MedicinePlanState> medicines,
        DateTimeOffset now,
        TimeZoneInfo zone,
        NotificationPlanOptions? options = null)
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
            }
            // Inactive medicines included: their packages stay in the
            // cabinet (PackageExpiryNotices).
            PlanPackages(state, stock, today, localNow, zone, options, result);
        }

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

            result.Add(new PlannedNotification(PlannedNotificationKind.LowStock, medicine.Id, day,
                DailyFireAt(day, i, localNow, zone, options), stage));
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
                PackageIds: [.. pending.Select(k => k.PackageId)]));
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
