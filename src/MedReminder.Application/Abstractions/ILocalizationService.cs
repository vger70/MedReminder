using System.Globalization;

namespace MedReminder.Application.Abstractions;

// Servizio di localizzazione delle stringhe UI, email e report
// (Incremento 16). Le stringhe sono chiavi dotted con namespace
// (es. "Ui.MainForm.Menu.File", "Notifications.Email.Subject") e
// vivono in file JSON per-lingua embedded nell'assembly UI, con
// override opzionale su disco per personalizzazioni utente avanzate.
//
// Regole di fallback:
//   1. Cerca la chiave nel dizionario della lingua richiesta.
//   2. Se assente, cerca nella lingua di default (SupportedLanguages.Default = "en").
//   3. Se anche lì è assente, ritorna la chiave stessa fra parentesi
//      quadre (es. "[Ui.MainForm.Menu.File]") — visibile in UI così
//      lo sviluppatore individua subito le lacune. Mai eccezione.
public interface ILocalizationService
{
    // Lingua attualmente selezionata dall'utente (dallo user.settings.
    // json). Non cambia a runtime — dopo un save la modifica ha effetto
    // dal riavvio.
    string CurrentLanguage { get; }

    // Traduce la chiave nella lingua corrente. Params sono passati a
    // string.Format con la CultureInfo della lingua corrente.
    string Get(string key, params object?[] args);

    // Overload che forza una lingua specifica. Usato dal MedicationMonitor:
    //   - email → GetIn(userLang, key)   (lingua utente scelta)
    //   - toast → GetIn(systemLang, key) (lingua di sistema Windows)
    string GetIn(string languageCode, string key, params object?[] args);

    // CultureInfo della lingua corrente. Usata per formattare numeri,
    // date, quando serve coerenza con il testo tradotto.
    CultureInfo CurrentCulture { get; }
}
