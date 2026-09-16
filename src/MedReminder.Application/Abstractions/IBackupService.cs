namespace MedReminder.Application.Abstractions;

// Backup del database utente (spec §23, Incremento 11).
// L'export copia il DB corrente in una posizione scelta dall'utente
// usando l'API online-backup di SQLite (safe hot backup senza fermare
// l'app); l'import lo ripristina dopo aver rilasciato i lock delle
// connessioni pool-ate. Retention: rimozione dei file più vecchi di
// N giorni dalla stessa cartella.
public interface IBackupService
{
    string DatabasePath { get; }

    // Copia il DB nella cartella indicata (nome file derivato da
    // timestamp UTC). Ritorna il path del file creato.
    Task<string> ExportAsync(string destinationDirectory, CancellationToken cancellationToken);

    // Rimuove i file "medreminder-*.db" più vecchi di retentionDays
    // dalla cartella indicata. Ritorna il numero di file cancellati.
    // Silenzioso se la cartella non esiste o è vuota; NON tocca file
    // che non matchano il pattern (utente potrebbe averci messo altro).
    Task<int> PruneOldBackupsAsync(
        string directory, int retentionDays, CancellationToken cancellationToken);

    // Sostituisce il DB corrente con quello indicato. Il caller (UI)
    // deve fermare monitor e scheduler PRIMA; l'implementazione forza
    // ClearAllPools() su Microsoft.Data.Sqlite per rilasciare gli
    // handle aperti dal DbContext pooling.
    Task ImportAsync(string sourceFilePath, CancellationToken cancellationToken);
}
