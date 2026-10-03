using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Packages;

// Package expiry notices (docs/analysis/ANALYSIS-PACKAGE-EXPIRY.md §4):
// "expiring soon" once the lead days start, "expired" the day after the
// effective expiry, each once per package, effective expiry and channel
// on this device. A package first seen already expired gets the expired
// notice only. Used-up and closed packages are never notified.
//
// Inactive medicines are included: their packages are still in the
// cabinet. The medicine's channels apply; NotificationChannels.None
// opts out. One toast per medicine and pass; one email per pass for all
// medicines, only where this device sends email (the master device of a
// shared installation). Each channel records its own notices, so a
// failed email is retried on the next pass without showing the toast
// again. A text names the expired packages of the medicine when it has
// any, otherwise the ones expiring soon; only the packages it names are
// recorded, so the others get their own notice on a later pass.
//
// Called by MedicationMonitor inside its WriteGate pass; the caller
// saves. Only ids are logged: no medicine name, no batch.
public sealed class PackageExpiryNotices
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockPackageRepository _packages;
    private readonly IStockMovementRepository _stock;
    private readonly IPackageExpiryNoticeEventRepository _events;
    private readonly IEmailNotificationService _email;
    private readonly IWindowsNotificationService _windows;
    private readonly TimeProvider _clock;
    private readonly ILogger<PackageExpiryNotices> _log;
    private readonly ILocalizationService? _localization;
    private readonly IProfileSettingsStore? _settings;

    public PackageExpiryNotices(
        IMedicineRepository medicines,
        IStockPackageRepository packages,
        IStockMovementRepository stock,
        IPackageExpiryNoticeEventRepository events,
        IEmailNotificationService email,
        IWindowsNotificationService windows,
        TimeProvider clock,
        ILogger<PackageExpiryNotices> log,
        ILocalizationService? localization = null,
        IProfileSettingsStore? settings = null)
    {
        _medicines = medicines;
        _packages = packages;
        _stock = stock;
        _events = events;
        _email = email;
        _windows = windows;
        _clock = clock;
        _log = log;
        _localization = localization;
        _settings = settings;
    }

    private sealed record Due(PackageListItem Item, int Stage);

    private sealed record MedicineNotice(Medicine Medicine, IReadOnlyList<Due> Due, string Body);

    // Returns the number of notices recorded (one per package and
    // channel). The caller saves.
    public async Task<int> RunAsync(DateOnly today, bool sendsEmail, CancellationToken cancellationToken)
    {
        // Read once, and only when some medicine has packages.
        PackageLeadDays? leadDays = null;
        var forEmail = new List<MedicineNotice>();
        var count = 0;

        foreach (var medicine in await _medicines.ListAllAsync(cancellationToken))
        {
            var channels = medicine.NotificationChannels;
            if (!sendsEmail) channels &= ~NotificationChannels.Email;
            if (channels == NotificationChannels.None) continue;
            var packages = await _packages.ListForMedicineAsync(medicine.Id, cancellationToken);
            if (packages.Count == 0) continue;

            leadDays ??= PackageSettings.LeadDays(_settings);
            var stock = MedicineStock.Current(await _stock.ListForMedicineAsync(medicine.Id, cancellationToken));
            var candidates = DueOf(PackageListQuery.Build(packages, stock, today, leadDays));
            if (candidates.Count == 0) continue;

            if ((channels & NotificationChannels.Windows) != 0)
            {
                var due = await PendingAsync(candidates, NotificationChannels.Windows, cancellationToken);
                if (due.Count > 0)
                {
                    var (title, body) = BuildTexts(medicine, due);
                    try
                    {
                        await _windows.ShowAsync(title, body, NotificationTarget.PackageExpiry(medicine.Id),
                            cancellationToken);
                        count += await RecordAsync(medicine, due, NotificationChannels.Windows, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Package expiry toast failed for medicine {MedicineId}", medicine.Id);
                    }
                }
            }
            if ((channels & NotificationChannels.Email) != 0)
            {
                var due = await PendingAsync(candidates, NotificationChannels.Email, cancellationToken);
                if (due.Count > 0) forEmail.Add(new MedicineNotice(medicine, due, BuildTexts(medicine, due).Body));
            }
        }

        if (forEmail.Count > 0)
        {
            try
            {
                await _email.SendAsync(BuildEmail(forEmail), cancellationToken);
                foreach (var notice in forEmail)
                {
                    count += await RecordAsync(notice.Medicine, notice.Due, NotificationChannels.Email, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Package expiry email failed for {Count} medicine(s)", forEmail.Count);
            }
        }
        return count;
    }

    private static List<Due> DueOf(IEnumerable<PackageListItem> items)
        => [.. items
            .Where(i => i.EffectiveExpiry is not null)
            .Select(i => new Due(i, i.Status switch
            {
                PackageExpiryStatus.Expired => PackageExpiryNoticeEvent.ExpiredStage,
                PackageExpiryStatus.ExpiringSoon => PackageExpiryNoticeEvent.SoonStage,
                _ => 0,
            }))
            .Where(d => d.Stage != 0)];

    // The candidates this channel has not notified yet: the expired ones
    // when there are any, otherwise those expiring soon.
    private async Task<IReadOnlyList<Due>> PendingAsync(IReadOnlyList<Due> candidates, NotificationChannels channel,
        CancellationToken cancellationToken)
    {
        var pending = new List<Due>();
        foreach (var due in candidates)
        {
            if (!await _events.ExistsAsync(due.Item.Package.Id, due.Item.EffectiveExpiry!.Value, due.Stage, channel,
                    cancellationToken))
            {
                pending.Add(due);
            }
        }
        return pending.Any(d => d.Stage == PackageExpiryNoticeEvent.ExpiredStage)
            ? [.. pending.Where(d => d.Stage == PackageExpiryNoticeEvent.ExpiredStage)]
            : pending;
    }

    private async Task<int> RecordAsync(Medicine medicine, IReadOnlyList<Due> due, NotificationChannels channel,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        foreach (var d in due)
        {
            await _events.AddAsync(new PackageExpiryNoticeEvent
            {
                PackageId = d.Item.Package.Id,
                MedicineId = medicine.Id,
                EffectiveExpiry = d.Item.EffectiveExpiry!.Value,
                Stage = d.Stage,
                Channel = channel,
                FiredAt = now,
            }, cancellationToken);
        }
        return due.Count;
    }

    // One text for the packages of one stage (PendingAsync never mixes
    // them): their number and the earliest date.
    private (string Title, string Body) BuildTexts(Medicine medicine, IReadOnlyList<Due> due)
    {
        var expired = due[0].Stage == PackageExpiryNoticeEvent.ExpiredStage;
        var date = due.Min(d => d.Item.EffectiveExpiry!.Value);
        var c = _localization?.CurrentCulture ?? System.Globalization.CultureInfo.CurrentCulture;
        var dateText = date.ToString("d", c);
        var many = due.Count > 1;
        if (_localization is null)
        {
            return expired
                ? ($"{medicine.Name}: a package has expired",
                    many
                        ? $"{due.Count} packages of {medicine.Name} have expired, the first on {dateText}. Check them and mark them in MedReminder."
                        : $"A package of {medicine.Name} expired on {dateText}. Check it and mark it in MedReminder.")
                : ($"{medicine.Name}: a package expires soon",
                    many
                        ? $"{due.Count} packages of {medicine.Name} expire soon, the first on {dateText}. Use them first or check them."
                        : $"A package of {medicine.Name} expires on {dateText}. Use it first or check it.");
        }
        var stage = expired ? "Expired" : "Soon";
        return (_localization.Get($"Notifications.PackageExpiry.{stage}.Title", medicine.Name),
            many
                ? _localization.Get($"Notifications.PackageExpiry.{stage}.Body.Many", medicine.Name, due.Count, dateText)
                : _localization.Get($"Notifications.PackageExpiry.{stage}.Body", medicine.Name, dateText));
    }

    private EmailMessage BuildEmail(IReadOnlyList<MedicineNotice> notices)
    {
        var subject = notices.Count == 1
            ? BuildTexts(notices[0].Medicine, notices[0].Due).Title
            : _localization?.Get("Notifications.PackageExpiry.Email.Subject") ?? "MedReminder: packages to check";
        var body = string.Join("\n", notices.Select(n => n.Body))
            + "\n\n" + (_localization?.Get("Notifications.Email.Footer")
                ?? "— MedReminder (organizational reminder, not a medical device).");
        return new EmailMessage(subject, body, Kind: EmailKind.PackageExpiry);
    }
}
