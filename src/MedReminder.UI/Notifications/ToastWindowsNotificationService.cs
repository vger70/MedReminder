using System.Runtime.Versioning;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace MedReminder.UI.Notifications;

// Modern Windows toasts via CommunityToolkit
// (Microsoft.Toolkit.Uwp.Notifications). ToastContentBuilder + Show()
// handle AUMID registration at runtime for unpackaged apps
// automatically (creates a Start-Menu shortcut + COM registration on
// first execution). No manual setup required.
//
// Replaces (in DI) TrayBalloonNotificationService as the primary
// IWindowsNotificationService implementation. On error (Windows 10
// pre-1809, feature disabled by policy, etc.) it falls back
// automatically to the tray icon's balloon so the notification is
// not lost.
//
// A notification with a target (EVOLUTION-PROPOSALS-2 §3.4) carries
// actions as toast arguments: the body opens the app on the medicine,
// a dose reminder offers "Remind me in 15 minutes" and a low-stock
// warning "Prepare request". Arguments hold identifiers only
// (NotificationActionArguments); ToastActivationRouter handles them.
[SupportedOSPlatform("windows10.0.17763.0")]
internal sealed class ToastWindowsNotificationService : IWindowsNotificationService
{
    private readonly TrayBalloonNotificationService _fallback;
    private readonly ILogger<ToastWindowsNotificationService> _log;
    private readonly ICurrentProfile? _profile;
    private readonly ILocalizationService? _localization;
    private bool _toastAvailable = true;

    public ToastWindowsNotificationService(
        TrayBalloonNotificationService fallback,
        ILogger<ToastWindowsNotificationService> log,
        ICurrentProfile? profile = null,
        ILocalizationService? localization = null)
    {
        _fallback = fallback;
        _log = log;
        _profile = profile;
        _localization = localization;
    }

    public Task ShowAsync(string title, string body, CancellationToken cancellationToken)
        => ShowCoreAsync(title, body, target: null, deliverAt: null, cancellationToken);

    public Task ShowAsync(string title, string body, NotificationTarget target, CancellationToken cancellationToken)
        => ShowCoreAsync(title, body, target, deliverAt: null, cancellationToken);

    // The same notification again at deliverAt, delivered by Windows even
    // if the app is closed by then (snooze).
    public Task ScheduleAsync(string title, string body, NotificationTarget target, DateTimeOffset deliverAt,
        CancellationToken cancellationToken)
        => ShowCoreAsync(title, body, target, deliverAt, cancellationToken);

    private async Task ShowCoreAsync(string title, string body, NotificationTarget? target, DateTimeOffset? deliverAt,
        CancellationToken cancellationToken)
    {
        if (_toastAvailable)
        {
            try
            {
                var builder = new ToastContentBuilder()
                    .AddText(title)
                    .AddText(body);
                if (target is not null && _profile is not null) AddActions(builder, target, _profile.Id);
                if (deliverAt is { } at) builder.Schedule(at);
                else builder.Show();
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Modern toast unavailable; switching to the tray-icon balloon for the rest of the session.");
                _toastAvailable = false;
                // fall through to the fallback
            }
        }

        // The balloon has no actions and cannot be scheduled: a snoozed
        // reminder is shown at once rather than lost.
        await _fallback.ShowAsync(title, body, cancellationToken);
    }

    private void AddActions(ToastContentBuilder builder, NotificationTarget target, string profileId)
    {
        foreach (var (key, value) in NotificationActionArguments.ForBody(target, profileId))
        {
            builder.AddArgument(key, value);
        }
        foreach (var action in NotificationActionArguments.ButtonsFor(target, profileId))
        {
            var button = new ToastButton().SetContent(ButtonText(action.Kind));
            foreach (var (key, value) in NotificationActionArguments.Format(action))
            {
                button.AddArgument(key, value);
            }
            // Snooze needs no window; the other actions open one.
            if (action.Kind == NotificationActionKind.Snooze) button.SetBackgroundActivation();
            builder.AddButton(button);
        }
    }

    private string ButtonText(NotificationActionKind kind) => kind switch
    {
        NotificationActionKind.Snooze => _localization?.Get(
            "Notifications.Action.Snooze", NotificationActionArguments.SnoozeMinutes)
            ?? $"Remind me in {NotificationActionArguments.SnoozeMinutes} minutes",
        NotificationActionKind.RequestPrescription => _localization?.Get("Notifications.Action.RequestPrescription")
            ?? "Prepare request",
        NotificationActionKind.OpenPrescriptions => _localization?.Get("Notifications.Action.OpenPrescription")
            ?? "Open the prescription",
        _ => kind.ToString(),
    };
}
