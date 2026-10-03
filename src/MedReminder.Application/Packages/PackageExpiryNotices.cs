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
// effective expiry, each once per package and effective expiry on this
// device. A package first seen already expired gets the expired notice
// only. Used-up and closed packages are never notified.
//
// Inactive medicines are included: their packages are still in the
// cabinet. The medicine's channels apply; NotificationChannels.None
// opts out. One toast per medicine and pass; one email per pass for all
// medicines, only where this device sends email (the master device of a
// shared installation). A notice is recorded when one of its channels
// succeeded; a failed attempt is retried on the next pass.
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

    private sealed record MedicineNotice(Medicine Medicine, IReadOnlyList<Due> Due, string Title, string Body);

    // Returns the number of packages notified. The caller saves.
    public async Task<int> RunAsync(DateOnly today, bool sendsEmail, CancellationToken cancellationToken)
    {
        var leadDays = PackageSettings.LeadDays(_settings);
        var notified = new List<MedicineNotice>();
        var forEmail = new List<MedicineNotice>();

        foreach (var medicine in await _medicines.ListAllAsync(cancellationToken))
        {
            var channels = medicine.NotificationChannels;
            if (channels == NotificationChannels.None) continue;
            var packages = await _packages.ListForMedicineAsync(medicine.Id, cancellationToken);
            if (packages.Count == 0) continue;

            var stock = MedicineStock.Current(await _stock.ListForMedicineAsync(medicine.Id, cancellationToken));
            var due = new List<Due>();
            foreach (var item in PackageListQuery.Build(packages, stock, today, leadDays))
            {
                if (item.EffectiveExpiry is not { } expiry) continue;
                var stage = item.Status switch
                {
                    PackageExpiryStatus.Expired => PackageExpiryNoticeEvent.ExpiredStage,
                    PackageExpiryStatus.ExpiringSoon => PackageExpiryNoticeEvent.SoonStage,
                    _ => 0,
                };
                if (stage == 0) continue;
                if (await _events.ExistsAsync(item.Package.Id, expiry, stage, cancellationToken)) continue;
                due.Add(new Due(item, stage));
            }
            if (due.Count == 0) continue;

            if (!sendsEmail) channels &= ~NotificationChannels.Email;
            if (channels == NotificationChannels.None) continue;

            var (title, body) = BuildTexts(medicine, due);
            var notice = new MedicineNotice(medicine, due, title, body);
            if ((channels & NotificationChannels.Windows) != 0)
            {
                try
                {
                    await _windows.ShowAsync(title, body, NotificationTarget.PackageExpiry(medicine.Id),
                        cancellationToken);
                    notified.Add(notice);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Package expiry toast failed for medicine {MedicineId}", medicine.Id);
                }
            }
            if ((channels & NotificationChannels.Email) != 0) forEmail.Add(notice);
        }

        if (forEmail.Count > 0)
        {
            try
            {
                await _email.SendAsync(BuildEmail(forEmail), cancellationToken);
                notified.AddRange(forEmail.Where(n => !notified.Contains(n)));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Package expiry email failed for {Count} medicine(s)", forEmail.Count);
            }
        }

        var now = _clock.GetUtcNow();
        var count = 0;
        foreach (var notice in notified)
        {
            foreach (var due in notice.Due)
            {
                await _events.AddAsync(new PackageExpiryNoticeEvent
                {
                    PackageId = due.Item.Package.Id,
                    MedicineId = notice.Medicine.Id,
                    EffectiveExpiry = due.Item.EffectiveExpiry!.Value,
                    Stage = due.Stage,
                    FiredAt = now,
                }, cancellationToken);
                count++;
            }
        }
        return count;
    }

    // The expired packages of the medicine first: one text with their
    // number and the earliest date.
    private (string Title, string Body) BuildTexts(Medicine medicine, IReadOnlyList<Due> due)
    {
        var expired = due.Where(d => d.Stage == PackageExpiryNoticeEvent.ExpiredStage).ToList();
        var shown = expired.Count > 0 ? expired : due.ToList();
        var date = shown.Min(d => d.Item.EffectiveExpiry!.Value);
        var c = _localization?.CurrentCulture ?? System.Globalization.CultureInfo.CurrentCulture;
        var dateText = date.ToString("d", c);
        var many = shown.Count > 1;
        if (_localization is null)
        {
            return expired.Count > 0
                ? ($"{medicine.Name}: a package has expired",
                    many
                        ? $"{shown.Count} packages of {medicine.Name} expired, the first on {dateText}. Check them and mark them in MedReminder."
                        : $"A package of {medicine.Name} expired on {dateText}. Check it and mark it in MedReminder.")
                : ($"{medicine.Name}: a package expires soon",
                    many
                        ? $"{shown.Count} packages of {medicine.Name} expire soon, the first on {dateText}. Use them first or check them."
                        : $"A package of {medicine.Name} expires on {dateText}. Use it first or check it.");
        }
        var stage = expired.Count > 0 ? "Expired" : "Soon";
        return (_localization.Get($"Notifications.PackageExpiry.{stage}.Title", medicine.Name),
            many
                ? _localization.Get($"Notifications.PackageExpiry.{stage}.Body.Many", medicine.Name, shown.Count, dateText)
                : _localization.Get($"Notifications.PackageExpiry.{stage}.Body", medicine.Name, dateText));
    }

    private EmailMessage BuildEmail(IReadOnlyList<MedicineNotice> notices)
    {
        var subject = notices.Count == 1
            ? notices[0].Title
            : _localization?.Get("Notifications.PackageExpiry.Email.Subject") ?? "MedReminder: packages to check";
        var body = string.Join("\n", notices.Select(n => n.Body))
            + "\n\n" + (_localization?.Get("Notifications.Email.Footer")
                ?? "— MedReminder (organizational reminder, not a medical device).");
        return new EmailMessage(subject, body, Kind: EmailKind.PackageExpiry);
    }
}
