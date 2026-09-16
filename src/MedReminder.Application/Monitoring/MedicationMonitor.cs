using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Monitoring;

// Orchestratore del controllo periodico (spec §18):
//  1. lancia il catch-up del consumo giornaliero;
//  2. per ogni medicina attiva calcola stock, rate, forecast;
//  3. valuta NotificationCycle.ShouldNotify;
//  4. spedisce sui canali configurati (Windows/Email) in modo isolato:
//     un fallimento email NON impedisce la toast e viceversa;
//  5. registra un NotificationEvent riassuntivo (Success = almeno un
//     canale ha funzionato);
//  6. commit.
//
// Non dipende da IHostedService: lo scheduler vive nell'UI (Incremento 5).
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
        ILocalizationService? localization = null)
    {
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

    public async Task<RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var today = LocalToday();
        var medicines = await _medicines.ListActiveAsync(cancellationToken);
        var sent = 0;

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

            if (!NotificationCycle.ShouldNotify(medicine, forecast.DaysRemaining, forecast.EstimatedRunOutDate, latest))
            {
                continue;
            }

            var daysRemaining = forecast.DaysRemaining!.Value;
            var eta = forecast.EstimatedRunOutDate;
            var dispatch = await DispatchAsync(medicine, currentStock, daysRemaining, eta, slots, cancellationToken);

            var evt = new NotificationEvent
            {
                MedicineId = medicine.Id,
                StockEpoch = medicine.StockEpoch,
                TriggeredAt = _clock.GetUtcNow(),
                Channel = dispatch.ChannelsAttempted,
                DaysRemainingAtSend = daysRemaining,
                Success = dispatch.AnyChannelSucceeded,
                ErrorMessage = dispatch.CombinedError,
            };
            await _notifications.AddAsync(evt, cancellationToken);

            if (dispatch.AnyChannelSucceeded) sent += 1;
        }

        await _uow.SaveChangesAsync(cancellationToken);
        return new RunResult(medicines.Count, sent);
    }

    private async Task<DispatchOutcome> DispatchAsync(
        Medicine medicine,
        decimal currentStock,
        int daysRemaining,
        DateOnly? eta,
        IReadOnlyList<MedicationAdministrationSlot> slots,
        CancellationToken cancellationToken)
    {
        var channels = medicine.NotificationChannels;
        var windowsSucceeded = false;
        var emailSucceeded = false;
        string? windowsError = null;
        string? emailError = null;

        if ((channels & NotificationChannels.Windows) != 0)
        {
            // Toast: lingua di SISTEMA (Windows), non quella scelta dall'utente
            // nell'app. NotificationTexts.BuildToast rileva la lingua sistema
            // internamente via CultureInfo.CurrentUICulture.
            var (title, body) = NotificationTexts.BuildToast(
                medicine, daysRemaining, localization: _localization);
            try
            {
                await _windows.ShowAsync(title, body, cancellationToken);
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
            // Email: lingua UTENTE (scelta nell'app). Il service la usa
            // come default via loc.Get(key).
            var message = NotificationTexts.BuildEmail(
                medicine, currentStock, daysRemaining, eta,
                administrationSlots: slots,
                localization: _localization);
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
        string? CombinedError);
}
