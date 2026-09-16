using System.Data;
using System.Text.RegularExpressions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace MedReminder.Infrastructure.Backup;

// Backup / restore del file DB.
//
// Export: usa l'API online backup di SQLite (SqliteConnection.BackupDatabase)
// che è thread-safe rispetto a scritture concorrenti — SQLite gestisce
// il locking a livello di page e produce un file di destinazione coerente
// anche se la scrittura di altre connessioni sta procedendo. È la
// tecnica raccomandata da SQLite (https://sqlite.org/backup.html)
// e sostituisce il File.Copy + WAL checkpoint che avevamo prima
// (il checkpoint riduceva ma non eliminava la finestra di torn write).
//
// Retention: pattern medreminder-*.db, mai altro (l'utente potrebbe aver
// messo file nella stessa cartella).
//
// Import: prima di sostituire il file corrente, forza il rilascio degli
// handle SQLite ancora in pool (ClearAllPools). Il caller (UI) deve
// comunque riavviare l'app subito dopo — l'IApplicationRestarter è
// pensato per questo.
internal sealed class BackupService : IBackupService
{
    private static readonly Regex BackupFileRegex =
        new(@"^medreminder-\d{8}-\d{6}\.db$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly MedReminderDbContext _db;
    private readonly TimeProvider _clock;

    public BackupService(MedReminderDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public string DatabasePath => AppDataPaths.GetDatabasePath();

    public async Task<string> ExportAsync(
        string destinationDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);

        var sourcePath = DatabasePath;
        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException(
                $"Il file di database '{sourcePath}' non esiste.");
        }

        var timestamp = _clock.GetUtcNow().ToString("yyyyMMdd-HHmmss");
        var destinationFile = Path.Combine(
            destinationDirectory, $"medreminder-{timestamp}.db");

        // Uso una connessione dedicata sul DB principale (read-write ma
        // in solo backup) anziché quella del DbContext scoped: evita
        // di lasciare stato nel connection pool condiviso e rende la
        // chiamata sicura anche se invocata fuori dal ciclo request DI.
        // Cache=Private per non condividere lo shared cache dell'app.
        var sourceConnectionString =
            new SqliteConnectionStringBuilder(AppDataPaths.BuildSqliteConnectionString())
            {
                Cache = SqliteCacheMode.Private,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

        var destConnectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = destinationFile,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

        await using var source = new SqliteConnection(sourceConnectionString);
        await using var dest = new SqliteConnection(destConnectionString);
        await source.OpenAsync(cancellationToken);
        await dest.OpenAsync(cancellationToken);

        // BackupDatabase è sincrona (non ha overload async in
        // Microsoft.Data.Sqlite): la chiamiamo su ThreadPool per non
        // bloccare l'eventuale UI thread caller.
        await Task.Run(() => source.BackupDatabase(dest), cancellationToken);

        _ = _db; // il campo resta iniettato per coerenza con la lifecycle scoped
        return destinationFile;
    }

    public Task<int> PruneOldBackupsAsync(
        string directory, int retentionDays, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (retentionDays <= 0) return Task.FromResult(0);
        if (!Directory.Exists(directory)) return Task.FromResult(0);

        var cutoff = _clock.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var deleted = 0;

        foreach (var path in Directory.EnumerateFiles(directory, "medreminder-*.db"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Doppio filtro: sia il glob che il regex esatto. Rifiuta
            // "medreminder-manual-x.db" o backup rinominati dall'utente:
            // non è nostro compito cancellarli.
            var name = Path.GetFileName(path);
            if (!BackupFileRegex.IsMatch(name)) continue;

            try
            {
                var lastWriteUtc = File.GetLastWriteTimeUtc(path);
                if (lastWriteUtc < cutoff)
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch
            {
                // Un file bloccato o senza permessi non deve interrompere
                // la potatura degli altri. Il fallimento è visibile solo
                // come "file rimasto in cartella".
            }
        }

        return Task.FromResult(deleted);
    }

    public Task ImportAsync(string sourceFilePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("File di backup non trovato.", sourceFilePath);
        }

        var target = DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Chiudi qualunque connessione ancora in pool con path == target,
        // altrimenti File.Move fallisce con "Sharing violation" su Windows.
        // ClearAllPools() è idempotente e non lancia se non ci sono pool.
        SqliteConnection.ClearAllPools();

        // Chiudi anche la connessione sottostante al DbContext scoped,
        // se il caller non l'ha già fatto (paranoia): il DbContext è
        // scoped ma il pooling di Microsoft.Data.Sqlite lavora sotto.
        try
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Closed)
            {
                conn.Close();
            }
        }
        catch
        {
            // Non blocchiamo l'import per un problema di chiusura
            // preventiva: ClearAllPools ha già fatto il grosso.
        }

        if (File.Exists(target))
        {
            var backupName = $"{target}.bak-{_clock.GetUtcNow():yyyyMMddHHmmss}";
            File.Move(target, backupName, overwrite: false);
        }
        File.Copy(sourceFilePath, target, overwrite: false);

        // Rimuovi eventuali file WAL/SHM residui del vecchio DB:
        // la nuova base è "pulita" e verrà ri-inizializzata da
        // DatabaseInitializer al prossimo avvio.
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var side = target + suffix;
            if (File.Exists(side))
            {
                try { File.Delete(side); } catch { /* ignora */ }
            }
        }

        _ = cancellationToken;
        return Task.CompletedTask;
    }
}
