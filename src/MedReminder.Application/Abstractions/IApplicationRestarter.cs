namespace MedReminder.Application.Abstractions;

// Restart auto-orchestrato dell'app: usato dopo un ripristino backup
// per rilasciare i lock SQLite sul DB nuovo e ricaricare la UI con
// dati coerenti.
//
// L'implementazione in UI lancia un nuovo processo dell'eseguibile
// corrente e chiude quello attuale. Il mutex Local\MedReminder.SingleInstance
// viene rilasciato dal processo uscente in tempo per il nuovo.
public interface IApplicationRestarter
{
    void RestartAndExit();
}
