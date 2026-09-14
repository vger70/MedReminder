namespace MedReminder.Application.Abstractions;

// Backup del database utente (spec §23). L'export copia il DB corrente
// in una posizione scelta dall'utente; l'import lo ripristina dopo
// conferma. L'implementazione Infrastructure gestisce il checkpoint WAL
// per garantire la coerenza del file esportato.
public interface IBackupService
{
    string DatabasePath { get; }

    // Copia il DB nella cartella indicata (nome file derivato da
    // timestamp). Ritorna il path del file creato.
    Task<string> ExportAsync(string destinationDirectory, CancellationToken cancellationToken);

    // Sostituisce il DB corrente con quello indicato. L'UI deve fermare
    // il monitor + chiudere i DbContext prima di chiamare l'import
    // (Incremento 7: hardening di shutdown pulito).
    Task ImportAsync(string sourceFilePath, CancellationToken cancellationToken);
}
