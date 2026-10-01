using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Monitoring;

// Orchestrator of the periodic check (spec §18):
//  1. run the daily-consumption catch-up;
//  2. for each active medicine, compute stock, rate, forecast;
//  3. evaluate NotificationCycle.StageToNotify (first warning at the
//     threshold, second at half of it while the stock is not
//     replenished);
//  4. dispatch to the configured channels (Windows / Email) in
//     isolation: an email failure does NOT prevent the toast and vice
//     versa;
//  5. record a summary NotificationEvent (Success = at least one
//     channel worked);
//  6. commit.
//
// Email across devices (household step H1): a successful email is also
// recorded as a SentEmailNotification, replicated to the other devices of
// the sync group (EmailNotificationSent). A device about to notify an
// epoch whose email another device already sent leaves the email channel
// out and still shows its own toast. Two devices that notify before
// either has synced still both send: only the master device of a later
// step removes that case.
//
// Does not depend on IHostedService: the scheduler lives in the UI
// (Increment 5).
public sealed class MedicationMonitor
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly INotificationEventRepository _notifications;
    private readonly IEmailNotificationService _email;
    private readonly IWindowsNotificationService _windows;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly ILogger<MedicationMonitor> _log;
    private readonly ILocalizationService? _localization;
    private readonly ISentEmailNotificationRepository? _sentEmails;
    private readonly IOperationLog? _operationLog;
    private readonly IMasterRole? _master;
    private readonly PrescriptionReminders? _prescriptionReminders;

    public MedicationMonitor(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationSuspensionRepository suspensions,
        IMedicationAdministrationSlotRepository slots,
        INotificationEventRepository notifications,
        IEmailNotificationService email,
        IWindowsNotificationService windows,
        IUnitOfWork uow,
        TimeProvider clock,
        ILogger<MedicationMonitor> log,
        ILocalizationService? localization = null,
        ISentEmailNotificationRepository? sentEmails = null,
        IOperationLog? operationLog = null,
        IMasterRole? master = null,
        PrescriptionReminders? prescriptionReminders = null)
    {
        _prescriptionReminders = prescriptionReminders;
        _master = master;
        _sentEmails = sentEmails;
        _operationLog = operationLog;
        _medicines = medicines;
        _stock = stock;
        _schedules = schedules;
        _suspensions = suspensions;
        _slots = slots;
        _notifications = notifications;
        _email = email;
        _windows = windows;
        _uow = uow;
        _clock = clock;
        _log = log;
        _localization = localization;
    }

    public sealed record RunResult(int MedicinesInspected, int NotificationsSent);

    // Serialized with ConsumptionCatchUp through WriteGate, so two
    // concurrent passes cannot both decide to notify the same epoch.
    public Task<RunResult> RunAsync(CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(RunCoreAsync, cancellationToken);

    private async Task<RunResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        var today = LocalToday();
        var medicines = await _medicines.ListActiveAsync(cancellationToken);
        var sent = 0;
        // Household step H4a: only the master sends email.
        var sendsEmail = _master is null || await _master.SendsEmailAsync(cancellationToken);

        foreach (var medicine in medicines)
        {
            var movements = await _stock.ListForMedicineAsync(medicine.Id, cancellationToken);
            var currentStock = MedicineStock.Current(movements);

            var schedule = await _schedules.ListForMedicineAsync(medicine.Id, cancellationToken);
            var slots = await _slots.ListForMedicineAsync(medicine.Id, cancellationToken);
            var rate = DailyConsumption.RateOn(today, schedule, slots);

            var suspensions = await _suspensions.ListForMedicineAsync(medicine.Id, cancellationToken);
            var isSuspended = SuspensionState.IsSuspendedOn(today, suspensions);

            var forecast = RunOutForecast.Compute(today, currentStock, rate, isSuspended);
            var latest = await _notifications.GetLatestForMedicineAsync(medicine.Id, cancellationToken);

            if (NotificationCycle.StageToNotify(medicine, forecast.DaysRemaining, forecast.EstimatedRunOutDate, latest)
                is not { } stage)
            {
                continue;
            }

            var daysRemaining = forecast.DaysRemaining!.Value;
            var eta = forecast.EstimatedRunOutDate;
            var channels = medicine.NotificationChannels;
            var emailSentElsewhere = false;
            if ((channels & NotificationChannels.Email) != 0 && _sentEmails is not null
                && NotificationCycle.EmailAlreadySent(medicine,
                    await _sentEmails.GetLatestForMedicineAsync(medicine.Id, cancellationToken), stage))
            {
                channels &= ~NotificationChannels.Email;
                emailSentElsewhere = true;
            }
            else if ((channels & NotificationChannels.Email) != 0 && !sendsEmail)
            {
                // The master sends it: for this device the channel is done.
                channels &= ~NotificationChannels.Email;
                emailSentElsewhere = true;
            }
            var dispatch = await DispatchAsync(medicine, stage, channels, currentStock, daysRemaining, eta, slots,
                cancellationToken);
            if (dispatch.EmailSucceeded) await RecordEmailSentAsync(medicine, stage, cancellationToken);

            var evt = new NotificationEvent
            {
                MedicineId = medicine.Id,
                StockEpoch = medicine.StockEpoch,
                EpochFactId = medicine.StockEpochFactId,
                TriggeredAt = _clock.GetUtcNow(),
                Channel = dispatch.ChannelsAttempted,
                DaysRemainingAtSend = daysRemaining,
                // An email another device sent, or left to the master,
                // closes this epoch's cycle like a successful channel of
                // this device.
                Success = dispatch.AnyChannelSucceeded || emailSentElsewhere,
                ErrorMessage = dispatch.CombinedError,
                Stage = stage,
            };
            await _notifications.AddAsync(evt, cancellationToken);

            if (dispatch.AnyChannelSucceeded) sent += 1;
        }

        // Prescriptions to collect before they lapse (EVOLUTION-PROPOSALS-2
        // §3.2); not counted in NotificationsSent, which is about stock.
        if (_prescriptionReminders is not null)
        {
            await _prescriptionReminders.RunAsync(today, sendsEmail, cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);
        return new RunResult(medicines.Count, sent);
    }

    private async Task RecordEmailSentAsync(Medicine medicine, int stage, CancellationToken cancellationToken)
    {
        if (_sentEmails is null) return;
        var record = new SentEmailNotification
        {
            MedicineId = medicine.Id,
            StockEpoch = medicine.StockEpoch,
            EpochFactId = medicine.StockEpochFactId,
            SentAt = _clock.GetUtcNow(),
            Stage = stage,
        };
        await _sentEmails.AddAsync(record, cancellationToken);
        // Recorded only while sync is enabled (OperationLog).
        if (_operationLog is not null)
        {
            await _operationLog.AppendAsync([Operations.EmailSent(record)], cancellationToken);
        }
    }

    private async Task<DispatchOutcome> DispatchAsync(
        Medicine medicine,
        int stage,
        NotificationChannels channels,
        decimal currentStock,
        int daysRemaining,
        DateOnly? eta,
        IReadOnlyList<MedicationAdministrationSlot> slots,
        CancellationToken cancellationToken)
    {
        var windowsSucceeded = false;
        var emailSucceeded = false;
        string? windowsError = null;
        string? emailError = null;

        if ((channels & NotificationChannels.Windows) != 0)
        {
            // Toast: USER language (chosen in the app), like the email.
            var (title, body) = NotificationTexts.BuildToast(
                medicine, daysRemaining, localization: _localization, stage: stage);
            try
            {
                await _windows.ShowAsync(title, body, NotificationTarget.LowStock(medicine.Id), cancellationToken);
                windowsSucceeded = true;
            }
            catch (Exception ex)
            {
                windowsError = ex.Message;
                _log.LogWarning(ex,
                    "Toast notification failed for medicine {MedicineId}", medicine.Id);
            }
        }

        if ((channels & NotificationChannels.Email) != 0)
        {
            // Email: USER language (chosen in the app). The service
            // uses it as the default via loc.Get(key).
            var message = NotificationTexts.BuildEmail(
                medicine, currentStock, daysRemaining, eta,
                administrationSlots: slots,
                localization: _localization,
                stage: stage);
            try
            {
                await _email.SendAsync(message, cancellationToken);
                emailSucceeded = true;
            }
            catch (Exception ex)
            {
                emailError = ex.Message;
                _log.LogWarning(ex,
                    "Email notification failed for medicine {MedicineId}", medicine.Id);
            }
        }

        string? combinedError = (windowsError, emailError) switch
        {
            (null, null) => null,
            (null, var e) => e,
            (var w, null) => w,
            var (w, e) => $"Windows: {w}; Email: {e}",
        };

        return new DispatchOutcome(
            ChannelsAttempted: channels,
            AnyChannelSucceeded: windowsSucceeded || emailSucceeded,
            EmailSucceeded: emailSucceeded,
            CombinedError: combinedError);
    }

    private DateOnly LocalToday()
    {
        var now = _clock.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(now, _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private sealed record DispatchOutcome(
        NotificationChannels ChannelsAttempted,
        bool AnyChannelSucceeded,
        bool EmailSucceeded,
        string? CombinedError);
}
