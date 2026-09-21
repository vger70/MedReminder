using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Monitoring;

// Per-tick logic for A5 dose-time reminders. Called by
// DoseReminderHostedService once per minute inside the active
// profile's IHost scope. Each call must be idempotent: a row in
// DoseReminderEvents with key (MedicineId, SlotKey, LocalDate)
// prevents re-firing the same reminder (ANALYSIS-A5 §4.2).
//
// Architecture note: mirrors MedicationMonitor — Application logic
// with no direct dependency on scheduling infrastructure. The hosted
// service is in MedReminder.UI/Hosting.
public sealed class DoseReminderService
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IDoseReminderEventRepository _events;
    private readonly IEmailNotificationService _email;
    private readonly IWindowsNotificationService _windows;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly ILogger<DoseReminderService> _log;
    private readonly ILocalizationService? _localization;

    // Grace window (ANALYSIS-A5 §4.3): a slot older than this is
    // considered "missed" and silently dropped. Configurable via
    // DoseReminderService (appsettings.json DoseReminder:GraceWindowMinutes).
    public static readonly TimeSpan DefaultGraceWindow = TimeSpan.FromMinutes(30);

    // Retention horizon for DoseReminderEvents rows (ANALYSIS-A5 §3.3):
    // kept only to deduplicate within the day, so a short window is
    // enough. Aligned with the 30-day log retention.
    private const int RetentionDays = 30;

    private readonly TimeSpan _graceWindow;

    public DoseReminderService(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IMedicationAdministrationSlotRepository slots,
        IDoseReminderEventRepository events,
        IEmailNotificationService email,
        IWindowsNotificationService windows,
        IUnitOfWork uow,
        TimeProvider clock,
        ILogger<DoseReminderService> log,
        ILocalizationService? localization = null,
        TimeSpan? graceWindow = null)
    {
        _medicines = medicines;
        _stock = stock;
        _schedules = schedules;
        _suspensions = suspensions;
        _slots = slots;
        _events = events;
        _email = email;
        _windows = windows;
        _uow = uow;
        _clock = clock;
        _log = log;
        _localization = localization;
        _graceWindow = graceWindow ?? DefaultGraceWindow;
    }

    public sealed record RunResult(int FiredCount);

    public async Task<RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetLocalNow();
        var today = DateOnly.FromDateTime(now.DateTime);
        var fired = 0;

        var medicines = await _medicines.ListActiveWithDoseReminderAsync(cancellationToken);

        foreach (var medicine in medicines)
        {
            var movements = await _stock.ListForMedicineAsync(medicine.Id, cancellationToken);
            var currentStock = MedicineStock.Current(movements);

            // Gate: stock zero — hard off for the rest of the day
            // (ANALYSIS-A5 §4.4).
            if (currentStock <= 0) continue;

            var schedule = await _schedules.ListForMedicineAsync(medicine.Id, cancellationToken);
            var slots = await _slots.ListForMedicineAsync(medicine.Id, cancellationToken);

            // Gate: therapy not active today (StartDate/EndDate,
            // schedule inactive, suspension).
            var rate = DailyConsumption.RateOn(today, schedule, slots);
            if (rate == 0m) continue;

            var suspensions = await _suspensions.ListForMedicineAsync(medicine.Id, cancellationToken);
            if (SuspensionState.IsSuspendedOn(today, suspensions)) continue;

            // Evaluate each timed slot.
            foreach (var slot in slots.Where(s => s.Time.HasValue))
            {
                var slotTime = slot.Time!.Value;
                var fireLocal = new DateTimeOffset(
                    today.ToDateTime(slotTime),
                    _clock.LocalTimeZone.GetUtcOffset(today.ToDateTime(slotTime)));

                // Too early: slot hasn't arrived yet.
                if (fireLocal > now) continue;

                // Grace window: slot is in the past beyond the window —
                // treat as missed, do NOT write a dedup row (ANALYSIS-A5 §4.3).
                if (now - fireLocal > _graceWindow) continue;

                var slotKey = slot.Id.ToString();
                if (await _events.ExistsAsync(medicine.Id, slotKey, today, cancellationToken))
                {
                    continue;
                }

                // Dispatch notification.
                var channels = await DispatchAsync(medicine, slotTime, cancellationToken);
                if (channels == NotificationChannels.None) continue;

                await _events.AddAsync(new DoseReminderEvent
                {
                    MedicineId = medicine.Id,
                    SlotKey = slotKey,
                    LocalDate = today,
                    FiredAt = _clock.GetUtcNow(),
                    Channel = channels,
                }, cancellationToken);

                fired++;
            }
        }

        if (fired > 0)
        {
            await _uow.SaveChangesAsync(cancellationToken);
            _log.LogInformation("Dose reminder tick: {Fired} reminders fired.", fired);
        }

        // Retention prune: keep only the last 30 days of records.
        // Run on every tick; PruneOlderThanAsync issues an immediate
        // ExecuteDelete (no SaveChanges needed) and is a cheap no-op
        // when nothing is old enough.
        var cutoff = today.AddDays(-RetentionDays);
        await _events.PruneOlderThanAsync(cutoff, cancellationToken);

        return new RunResult(fired);
    }

    private async Task<NotificationChannels> DispatchAsync(
        Medicine medicine,
        TimeOnly slotTime,
        CancellationToken cancellationToken)
    {
        var channels = medicine.NotificationChannels;
        var dispatched = NotificationChannels.None;

        if ((channels & NotificationChannels.Windows) != 0)
        {
            var (title, body) = NotificationTexts.BuildDoseReminder(
                medicine, slotTime, localization: _localization);
            try
            {
                await _windows.ShowAsync(title, body, cancellationToken);
                dispatched |= NotificationChannels.Windows;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Dose reminder toast failed for medicine {MedicineId}.", medicine.Id);
            }
        }

        if ((channels & NotificationChannels.Email) != 0)
        {
            var (subject, emailBody) = NotificationTexts.BuildDoseReminder(
                medicine, slotTime, localization: _localization);
            var msg = new EmailMessage(subject, emailBody);
            try
            {
                await _email.SendAsync(msg, cancellationToken);
                dispatched |= NotificationChannels.Email;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Dose reminder email failed for medicine {MedicineId}.", medicine.Id);
            }
        }

        return dispatched;
    }
}
