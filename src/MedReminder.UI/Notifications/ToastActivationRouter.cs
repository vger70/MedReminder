using System.Runtime.Versioning;
using MedReminder.Application.Notifications;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;

namespace MedReminder.UI.Notifications;

// Receives the clicks on toasts and their buttons (EVOLUTION-PROPOSALS-2
// §3.4). Subscribed at the very start of the process, because Windows
// may launch the app for a click when it is not running: the action is
// then kept until the main window attaches its handler. With the app
// running, the toolkit raises OnActivated in this process.
//
// Arguments are identifiers only (NotificationActionArguments) and are
// not logged. Unknown arguments, from a newer version, give a null
// action: the handler only brings the window forward.
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class ToastActivationRouter
{
    private static readonly object Gate = new();
    private static Action<NotificationAction?>? _handler;
    private static bool _hasPending;
    private static NotificationAction? _pending;

    // True when Windows launched this process for a toast click.
    public static bool LaunchedByToast { get; private set; }

    public static void Initialize()
    {
        try
        {
            LaunchedByToast = ToastNotificationManagerCompat.WasCurrentProcessToastActivated();
            ToastNotificationManagerCompat.OnActivated += OnActivated;
        }
        catch (Exception ex)
        {
            // Toasts unavailable (old Windows, policy): the app still
            // starts; notifications fall back to the tray balloon.
            Log.Warning(ex, "Toast activation is not available.");
        }
    }

    // The handler runs on the toolkit's thread: it marshals to the UI
    // thread itself. A click received before it attached is replayed.
    public static void Attach(Action<NotificationAction?> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        bool replay;
        NotificationAction? pending;
        lock (Gate)
        {
            _handler = handler;
            replay = _hasPending;
            pending = _pending;
            _hasPending = false;
            _pending = null;
        }
        if (replay) handler(pending);
    }

    public static void Detach()
    {
        lock (Gate) _handler = null;
    }

    private static void OnActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        NotificationAction? action;
        try
        {
            var args = ToastArguments.Parse(e.Argument ?? string.Empty);
            action = NotificationActionArguments.Parse(args.ToDictionary(p => p.Key, p => p.Value ?? string.Empty));
        }
        catch (Exception ex)
        {
            Log.Warning("Toast arguments could not be read ({ExceptionType}).", ex.GetType().Name);
            action = null;
        }

        Action<NotificationAction?>? handler;
        lock (Gate)
        {
            handler = _handler;
            if (handler is null)
            {
                _hasPending = true;
                _pending = action;
                return;
            }
        }
        handler(action);
    }
}
