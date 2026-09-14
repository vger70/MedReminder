using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Persistence;

// Inizializzatore idempotente del database:
//  1. crea il file DB e lo schema se assenti (EnsureCreated per MVP;
//     Incremento 8 introdurrà le vere Migrations);
//  2. imposta PRAGMA journal_mode = WAL per resistere a chiusure
//     improvvise (ANALYSIS §1.1 punto 14);
//  3. imposta foreign_keys = ON (SQLite le disabilita di default).
public sealed class DatabaseInitializer
{
    private readonly MedReminderDbContext _db;
    private readonly ILogger<DatabaseInitializer> _log;

    public DatabaseInitializer(MedReminderDbContext db, ILogger<DatabaseInitializer> log)
    {
        _db = db;
        _log = log;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var created = await _db.Database.EnsureCreatedAsync(cancellationToken);
        if (created)
        {
            _log.LogInformation("MedReminder database created.");
        }

        await _db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
        await _db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", cancellationToken);
        await _db.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;", cancellationToken);
    }
}
