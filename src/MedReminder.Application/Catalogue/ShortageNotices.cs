using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Catalogue;

// Device-local log of the shortage notices shown, keyed on
// (MedicineId, Code, Start).
public interface IShortageNoticeEventRepository
{
    Task<bool> ExistsAsync(Guid medicineId, string code, DateOnly start, CancellationToken cancellationToken);

    Task AddAsync(ShortageNoticeEvent notice, CancellationToken cancellationToken);
}

// Tells the user once when an active medicine's package enters the
// shortage list, or when a shortage is announced for it
// (docs/notes/EVOLUTION-PROPOSALS-2.md §3.3), on the medicine's
// channels; email only where this device sends email. Called by
// MedicationMonitor inside its WriteGate pass; the caller saves.
public sealed class ShortageNotices
{
    private readonly IShortageListStore _store;
    private readonly IShortageNoticeEventRepository _events;
    private readonly IMedicineRepository _medicines;
    private readonly IEmailNotificationService _email;
    private readonly IWindowsNotificationService _windows;
    private readonly ILocalizationService _localization;
    private readonly TimeProvider _clock;
    private readonly ILogger<ShortageNotices> _log;

    public ShortageNotices(
        IShortageListStore store,
        IShortageNoticeEventRepository events,
        IMedicineRepository medicines,
        IEmailNotificationService email,
        IWindowsNotificationService windows,
        ILocalizationService localization,
        TimeProvider clock,
        ILogger<ShortageNotices> log)
    {
        _store = store;
        _events = events;
        _medicines = medicines;
        _email = email;
        _windows = windows;
        _localization = localization;
        _clock = clock;
        _log = log;
    }

    public async Task<int> RunAsync(DateOnly today, bool sendsEmail, CancellationToken cancellationToken)
    {
        if (_store.Load() is not { } list) return 0;
        var sent = 0;
        foreach (var medicine in await _medicines.ListActiveAsync(cancellationToken))
        {
            if (list.NoticeFor(medicine.NationalCode, today) is not { } notice) continue;
            var code = notice.Entry.Code;
            if (await _events.ExistsAsync(medicine.Id, code, notice.Entry.Start, cancellationToken)) continue;

            var channels = medicine.NotificationChannels;
            if (!sendsEmail) channels &= ~NotificationChannels.Email;
            if (channels == NotificationChannels.None) continue;

            var (title, body) = ShortageTexts.Notification(medicine.Name, notice, _localization);
            var any = false;
            if ((channels & NotificationChannels.Windows) != 0)
            {
                try
                {
                    await _windows.ShowAsync(title, body, NotificationTarget.Shortage(medicine.Id), cancellationToken);
                    any = true;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Shortage notice toast failed for medicine {MedicineId}", medicine.Id);
                }
            }
            if ((channels & NotificationChannels.Email) != 0)
            {
                try
                {
                    var detail = ShortageTexts.Detail(notice, list.ListDate, _localization);
                    await _email.SendAsync(new EmailMessage(title,
                        body + "\n\n" + detail.Replace(Environment.NewLine, "\n") + "\n\n"
                        + _localization.Get("Notifications.Email.Footer")), cancellationToken);
                    any = true;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Shortage notice email failed for medicine {MedicineId}", medicine.Id);
                }
            }
            // A failed attempt is retried on the next pass.
            if (!any) continue;
            await _events.AddAsync(new ShortageNoticeEvent
            {
                MedicineId = medicine.Id,
                Code = code,
                Start = notice.Entry.Start,
                FiredAt = _clock.GetUtcNow(),
            }, cancellationToken);
            sent++;
        }
        return sent;
    }
}
