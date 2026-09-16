using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Persistence;

// Inizializzatore idempotente del database:
//  1. crea il file DB e lo schema se assenti (EnsureCreated per MVP;
//     una vera pipeline di migration verrà introdotta dopo l'MVP);
//  2. applica patch di schema idempotenti per colonne aggiunte in
//     incrementi successivi (evita che l'utente debba cancellare il DB
//     ad ogni feature che aggiunge una colonna);
//  3. imposta PRAGMA journal_mode=WAL, foreign_keys=ON, synchronous=NORMAL.
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
        else
        {
            await ApplyIdempotentSchemaPatchesAsync(cancellationToken);
        }

        await _db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
        await _db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", cancellationToken);
        await _db.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;", cancellationToken);
    }

    // Patch di schema per DB esistenti creati con versioni precedenti.
    // Ogni patch DEVE essere idempotente (safe da rieseguire su un DB
    // già aggiornato). Elenco ordinato cronologicamente delle patch:
    //
    //   1) Incremento 9c: aggiunta colonna Day (TEXT NOT NULL) a
    //      MedicationIntakes. Le righe pre-esistenti (nessuna in
    //      circolazione dato che la tabella non era usata) ricevono
    //      il default '0001-01-01'.
    private async Task ApplyIdempotentSchemaPatchesAsync(CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(
            table: "MedicationIntakes",
            column: "Day",
            typeSpec: "TEXT NOT NULL DEFAULT '0001-01-01'",
            cancellationToken);

        // Incremento 10: nuova tabella MedicationAdministrationSlots.
        // CREATE TABLE IF NOT EXISTS è idempotente: se il DB è nuovo,
        // EnsureCreatedAsync l'ha già creata (via ApplyConfiguration) e
        // questo comando è un no-op. Se il DB è pre-Incremento 10,
        // la tabella viene creata con lo stesso schema che EF Core
        // emetterebbe.
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""MedicationAdministrationSlots"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MedicationAdministrationSlots"" PRIMARY KEY,
                ""MedicineId"" TEXT NOT NULL,
                ""Dose"" TEXT NOT NULL,
                ""Time"" TEXT NULL,
                ""TimingLabel"" TEXT NULL,
                ""Order"" INTEGER NOT NULL,
                CONSTRAINT ""FK_MedicationAdministrationSlots_Medicines_MedicineId""
                    FOREIGN KEY (""MedicineId"") REFERENCES ""Medicines"" (""Id"") ON DELETE RESTRICT
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_MedicationAdministrationSlots_MedicineId""
                ON ""MedicationAdministrationSlots"" (""MedicineId"");", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_MedicationAdministrationSlots_MedicineId_Order""
                ON ""MedicationAdministrationSlots"" (""MedicineId"", ""Order"");", cancellationToken);
    }

    private async Task ExecuteRawSqlAsync(string sql, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private async Task<bool> AddColumnIfMissingAsync(
        string table, string column, string typeSpec, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (await ColumnExistsAsync(connection, table, column, cancellationToken))
        {
            return false;
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {typeSpec};";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _log.LogInformation("Added missing column {Column} to {Table}.", column, table);
        return true;
    }

    private static async Task<bool> ColumnExistsAsync(
        DbConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // PRAGMA table_info restituisce colonne:
            //   0=cid  1=name  2=type  3=notnull  4=dflt_value  5=pk
            var name = reader.GetString(1);
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
