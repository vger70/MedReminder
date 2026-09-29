using System.Reflection;

namespace MedReminder.UI;

// Application icon: loaded once from the embedded resource
// "MedReminder.UI.medreminder.ico" and reused by MedReminderFormBase
// (every form) and by ApplicationTrayIcon.
//
// System.Drawing.Icon loads every sub-resolution available in the ico
// from the stream; Windows picks the right size based on the context
// (title bar 16×16, alt-tab 32×32, taskbar 24 / 32, etc.).
//
// Keeping a single shared Icon is fine: it is NEVER disposed while
// the process is alive (the app owns the icon for its entire
// lifetime).
internal static class AppIcon
{
    private const string ResourceName = "MedReminder.UI.medreminder.ico";

    private static readonly Lazy<Icon?> Instance = new(LoadFromResource);

    // Application icon. Returns null if the resource cannot be found
    // (corrupted build): the caller must handle the fallback to the
    // default WinForms icon.
    public static Icon? Default => Instance.Value;

    private static Icon? LoadFromResource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null) return null;
        // Copy the stream: Icon(stream) reads lazily and closes when
        // done, but keeping a persistent MemoryStream inside a Lazy
        // is safer against premature disposal.
        var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        return new Icon(ms);
    }
}
