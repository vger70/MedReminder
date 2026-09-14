using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Backup;

internal sealed class BackupService : IBackupService
{
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

        // Forza il checkpoint WAL così il file principale è "completo" al
        // momento della copia (riduce a zero le probabilità di leggere un
        // WAL mai riflesso nel main DB).
        await _db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);

        var timestamp = _clock.GetUtcNow().ToString("yyyyMMdd-HHmmss");
        var destinationFile = Path.Combine(
            destinationDirectory, $"medreminder-{timestamp}.db");

        var sourcePath = DatabasePath;
        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException($"Il file di database '{sourcePath}' non esiste.");
        }

        // File.Copy è synchronous ma per file di piccole dimensioni tipiche
        // (KB..pochi MB) va bene. Su copie molto grandi si potrà passare a
        // uno stream copy asincrono.
        File.Copy(sourcePath, destinationFile, overwrite: false);
        return destinationFile;
    }

    public Task ImportAsync(string sourceFilePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("File di backup non trovato.", sourceFilePath);
        }

        // Prerequisito: il caller ha fermato il monitor, chiuso i DbContext
        // e rilasciato eventuali handle sul file corrente. Se il DB
        // esistente è ancora presente lo rinominiamo come .bak-<timestamp>
        // per non perdere i dati in caso di errore utente.
        var target = DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            var backupName = $"{target}.bak-{_clock.GetUtcNow():yyyyMMddHHmmss}";
            File.Move(target, backupName, overwrite: false);
        }
        File.Copy(sourceFilePath, target, overwrite: false);

        // Rimuovi anche eventuali file WAL/SHM residui: la nuova base è
        // "pulita" e verrà ri-inizializzata da DatabaseInitializer al
        // prossimo avvio.
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var side = target + suffix;
            if (File.Exists(side)) File.Delete(side);
        }

        _ = cancellationToken;
        return Task.CompletedTask;
    }
}
