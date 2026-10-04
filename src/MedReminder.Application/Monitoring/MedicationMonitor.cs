using MedReminder.Application.Abstractions;
using MedReminder.Application.Calendar;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Deadlines;
using MedReminder.Application.Notifications;
using MedReminder.Application.Packages;
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
    private readonly ShortageNotices? _shortageNotices;
    private readonly DeadlineReminders? _deadlineReminders;
    private readonly CaregiverDigest? _caregiverDigest;
    private readonly PackageExpiryNotices? _packageExpiryNotices;
    private readonly IPrescriptionRepository? _prescriptions;
    private readonly IPrescriptionDispensationRepository? _dispensations;

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
        PrescriptionReminders? prescriptionReminders = null,
        ShortageNotices? shortageNotices = null,
        DeadlineReminders? deadlineReminders = null,
        CaregiverDigest? caregiverDigest = null,
        PackageExpiryNotices? packageExpiryNotices = null,
        IPrescriptionRepository? prescriptions = null,
        IPrescriptionDispensationRepository? dispensations = null)
    {
        _prescriptions = prescriptions;
        _dispensations = dispensations;
        _caregiverDigest = caregiverDigest;
        _packageExpiryNotices = packageExpiryNotices;
        _deadlineReminders = deadlineReminders;
        _shortageNotices = shortageNotices;
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
            var repeatable = await RepeatableNoticeAsync(medicine.Id, today, cancellationToken);
            var dispatch = await DispatchAsync(medicine, stage, channels, currentStock, daysRemaining, eta, slots,
                repeatable, cancellationToken);
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
        // Medicines entering the shortage list (EVOLUTION-PROPOSALS-2 §3.3).
        if (_shortageNotices is not null)
        {
            await _shortageNotices.RunAsync(today, sendsEmail, cancellationToken);
        }
        // Administrative deadlines (EVOLUTION-PROPOSALS-2 §3.6).
        if (_deadlineReminders is not null)
        {
            await _deadlineReminders.RunAsync(today, sendsEmail, cancellationToken);
        }
        // Packages expiring or expired (ANALYSIS-PACKAGE-EXPIRY.md §4).
        if (_packageExpiryNotices is not null)
        {
            await _packageExpiryNotices.RunAsync(today, sendsEmail, cancellationToken);
        }
        // Weekly summary for the caregiver (EVOLUTION-PROPOSALS-2 §3.8).
        if (_caregiverDigest is not null)
        {
            await _caregiverDigest.RunAsync(today, sendsEmail, cancellationToken);
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
        RepeatablePrescriptionNotice? repeatable,
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
                medicine, daysRemaining, localization: _localization, stage: stage, repeatable: repeatable);
            try
            {
                await _windows.ShowAsync(title, body, NotificationTarget.LowStock(medicine.Id, repeatable is not null),
                    cancellationToken);
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
                stage: stage,
                repeatable: repeatable);
            // The run-out date as a calendar event (EVOLUTION-PROPOSALS-2
            // §3.7), with a generic title: calendars live on third-party
            // clouds.
            if (eta is { } runOut)
            {
                message = message with { CalendarEvent = CalendarEntries.RunOut(medicine.Id, runOut, null, _localization) };
            }
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

    // The repeatable prescription of the medicine to collect, with
    // dispensations left: the one that lapses first (no end date last).
    private async Task<RepeatablePrescriptionNotice?> RepeatableNoticeAsync(
        Guid medicineId, DateOnly today, CancellationToken cancellationToken)
    {
        if (_prescriptions is null || _dispensations is null) return null;
        var repeatable = (await _prescriptions.ListForMedicineAsync(medicineId, cancellationToken))
            .Where(p => p.IsRepeatable).ToList();
        if (repeatable.Count == 0) return null;
        var collected = (await _dispensations.ListForMedicineAsync(medicineId, cancellationToken))
            .GroupBy(d => d.PrescriptionId).ToDictionary(g => g.Key, g => g.Count());
        var open = repeatable
            .Select(p => (p, count: collected.GetValueOrDefault(p.Id)))
            .Where(x => x.p.StatusOn(today, x.count) == Domain.Prescriptions.PrescriptionStatus.ToCollect)
            .OrderBy(x => x.p.ValidUntil ?? DateOnly.MaxValue)
            .FirstOrDefault();
        return open.p is null
            ? null
            : new RepeatablePrescriptionNotice(
                Domain.Prescriptions.PrescriptionRules.DispensationsLeft(open.p, open.count), open.p.ValidUntil);
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
