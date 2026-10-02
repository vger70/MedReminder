using System.Globalization;
using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Overview;
using MedReminder.Application.Sync;
using MedReminder.Domain.Sync;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Notifications;

// Weekly stock summary for the caregiver (docs/notes/
// EVOLUTION-PROPOSALS-2.md §3.8): every active medicine with its stock,
// status and run-out date, nothing about doses taken or missed. Sent to
// the caregiver only, every CaregiverDigestFrequency.WeeklyDays days,
// by a device that sends email (the master device of a shared
// installation). The day it was sent is a replicated profile setting
// (CaregiverDigestSentOn), so the other devices of a synced profile do
// not send it again once they have synced.
//
// Called by MedicationMonitor inside its WriteGate pass; the caller
// saves. Neither the content nor the address is logged.
public sealed class CaregiverDigest
{
    private readonly IProfileSettingsStore _settings;
    private readonly MedicineOverviewLoader _overview;
    private readonly IEmailNotificationService _email;
    private readonly IOperationLog _operations;
    private readonly ILocalizationService _localization;
    private readonly ILogger<CaregiverDigest> _log;

    public CaregiverDigest(
        IProfileSettingsStore settings,
        MedicineOverviewLoader overview,
        IEmailNotificationService email,
        IOperationLog operations,
        ILocalizationService localization,
        ILogger<CaregiverDigest> log)
    {
        _settings = settings;
        _overview = overview;
        _email = email;
        _operations = operations;
        _localization = localization;
        _log = log;
    }

    // Whether a digest is due on that day with these settings.
    public static bool IsDue(IReadOnlyDictionary<string, string?> settings, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.GetValueOrDefault(ProfileSetting.CaregiverAddress))) return false;
        if (!CaregiverDigestFrequency.IsWeekly(settings.GetValueOrDefault(ProfileSetting.CaregiverDigest))) return false;
        return !TryParseDay(settings.GetValueOrDefault(ProfileSetting.CaregiverDigestSentOn), out var last)
            || today >= last.AddDays(CaregiverDigestFrequency.WeeklyDays);
    }

    // True when a digest was sent.
    public async Task<bool> RunAsync(DateOnly today, bool sendsEmail, CancellationToken cancellationToken)
    {
        if (!sendsEmail) return false;
        var settings = _settings.Read();
        if (!IsDue(settings, today)) return false;

        var profileName = settings.GetValueOrDefault(ProfileSetting.DisplayName) ?? string.Empty;
        var medicines = (await _overview.LoadAsync(cancellationToken)).Where(m => m.IsActive).ToList();
        var message = Build(profileName, today, medicines, _localization);
        try
        {
            await _email.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            // Tried again on the next pass.
            _log.LogWarning(ex, "Caregiver digest email failed");
            return false;
        }

        var sentOn = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await _operations.AppendAsync([new ProfileSettingChanged(ProfileSetting.CaregiverDigestSentOn, sentOn)],
            cancellationToken);
        _settings.Write(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ProfileSetting.CaregiverDigestSentOn] = sentOn,
        });
        return true;
    }

    public static EmailMessage Build(string profileName, DateOnly today, IReadOnlyList<MedicineListItem> medicines,
        ILocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(medicines);
        ArgumentNullException.ThrowIfNull(loc);
        var c = loc.CurrentCulture;
        var body = new StringBuilder();
        body.Append(loc.Get("Notifications.Digest.Intro", profileName, today.ToString("d", c))).Append("\n\n");
        if (medicines.Count == 0) body.Append(loc.Get("Notifications.Digest.Empty")).Append('\n');
        foreach (var m in medicines.OrderBy(m => m.DaysRemaining ?? int.MaxValue)
                     .ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var stock = string.Format(c, "{0:0.##} {1}", m.CurrentStock, m.Unit).TrimEnd();
            body.Append(m.EstimatedRunOutDate is { } runOut && m.DaysRemaining is { } days
                    ? loc.Get("Notifications.Digest.Line", m.Name, stock, m.StatusDisplay, days, runOut.ToString("d", c))
                    : loc.Get("Notifications.Digest.LineNoForecast", m.Name, stock, m.StatusDisplay))
                .Append('\n');
        }
        body.Append('\n').Append(loc.Get("Notifications.Digest.Why", profileName));
        body.Append("\n\n").Append(loc.Get("Notifications.Email.Footer"));
        return new EmailMessage(loc.Get("Notifications.Digest.Subject", profileName), body.ToString(),
            Kind: EmailKind.Digest);
    }

    private static bool TryParseDay(string? value, out DateOnly day)
        => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
}
