using System.Drawing;
using System.Reflection;

namespace MedReminder.UI;

// Icona applicazione: caricata una volta dall'embedded resource
// "MedReminder.UI.medreminder.ico" e riusata da MedReminderFormBase
// (tutte le form) e da ApplicationTrayIcon.
//
// System.Drawing.Icon carica dallo stream tutte le sotto-risoluzioni
// disponibili nell'ico; Windows sceglie la size giusta in base al
// contesto (title bar 16×16, alt-tab 32×32, taskbar 24/32, ecc.).
//
// Tenere un singolo Icon condiviso è ok: NON viene mai disposato
// finché il processo è vivo (l'app owns the icon per l'intera durata).
internal static class AppIcon
{
    private const string ResourceName = "MedReminder.UI.medreminder.ico";

    private static readonly Lazy<Icon?> Instance = new(LoadFromResource);

    // Icona applicazione. Ritorna null se il resource non è stato
    // trovato (build corrotta): il caller deve gestire il fallback
    // sull'icona default WinForms.
    public static Icon? Default => Instance.Value;

    private static Icon? LoadFromResource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null) return null;
        // Copia lo stream: Icon(stream) legge lazy e chiude quando finisce,
        // ma tenere il file MemoryStream persistente in un Lazy è più
        // sicuro contro dispose prematuri.
        var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        return new Icon(ms);
    }
}
