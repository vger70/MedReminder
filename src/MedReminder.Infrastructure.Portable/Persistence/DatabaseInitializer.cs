using System.Data.Common;
using MedReminder.Infrastructure.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Persistence;

// Idempotent database initializer:
//  1. creates the DB file and schema if missing (EnsureCreated for
//     the MVP; a proper migration pipeline will be introduced after
//     the MVP);
//  2. applies idempotent schema patches for columns added in later
//     increments (so the user does not have to delete the DB every
//     time a feature adds a column);
//  3. sets PRAGMA journal_mode=WAL, foreign_keys=ON,
//     synchronous=NORMAL.
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

        // The catalogue tables are intentionally kept out of the EF
        // model (ANALYSIS-DRUG-CATALOGUE.md §2.4). Their DDL runs
        // unconditionally on every boot so fresh databases and
        // pre-existing databases converge on the same schema without
        // an EnsureCreated shortcut for these tables.
        await ApplyCatalogueSchemaAsync(cancellationToken);

        await _db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
        await _db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", cancellationToken);
        await _db.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;", cancellationToken);
    }

    private async Task ApplyCatalogueSchemaAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        await CatalogueSchema.ApplyAsync(connection, cancellationToken);
    }

    // Schema patches for existing DBs created with earlier versions.
    // Every patch MUST be idempotent (safe to re-run on an
    // already-upgraded DB). Chronologically ordered list of patches:
    //
    //   1) Increment 9c: added the Day column (TEXT NOT NULL) on
    //      MedicationIntakes. Pre-existing rows (none in the wild
    //      since the table was not used) receive the default
    //      '0001-01-01'.
    private async Task ApplyIdempotentSchemaPatchesAsync(CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(
            table: "MedicationIntakes",
            column: "Day",
            typeSpec: "TEXT NOT NULL DEFAULT '0001-01-01'",
            cancellationToken);

        // Increment 10: new MedicationAdministrationSlots table.
        // CREATE TABLE IF NOT EXISTS is idempotent: if the DB is
        // new, EnsureCreatedAsync has already created it (via
        // ApplyConfiguration) and this command is a no-op. If the
        // DB is pre-Increment 10, the table is created with the
        // same schema EF Core would emit.
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

        // M1 (Reference catalogue): three optional columns extending
        // the existing Medicines table. Each ADD COLUMN is guarded by
        // a PRAGMA table_info check so this stays idempotent.
        await AddColumnIfMissingAsync(
            table: "Medicines",
            column: "NationalCode",
            typeSpec: "TEXT NULL",
            cancellationToken);
        await AddColumnIfMissingAsync(
            table: "Medicines",
            column: "AtcCode",
            typeSpec: "TEXT NULL",
            cancellationToken);
        await AddColumnIfMissingAsync(
            table: "Medicines",
            column: "LinkedReferenceMedicineId",
            typeSpec: "TEXT NULL",
            cancellationToken);

        // A1 (Complex therapy regimens): two additive columns on the
        // MedicationScheduleHistories table so each entry can carry a
        // discriminated Schedule shape (see ANALYSIS-A1-REGIMENS.md
        // §2.4). ScheduleKind defaults to 0 = FixedDaily; existing
        // rows read back with legacy semantics without a data-fix
        // pass. Idempotent through the PRAGMA table_info guard.
        await AddColumnIfMissingAsync(
            table: "MedicationScheduleHistories",
            column: "ScheduleKind",
            typeSpec: "INTEGER NOT NULL DEFAULT 0",
            cancellationToken);
        await AddColumnIfMissingAsync(
            table: "MedicationScheduleHistories",
            column: "SchedulePayload",
            typeSpec: "TEXT NULL",
            cancellationToken);

        // A5 (Dose-time reminder): flag on Medicines and the
        // DoseReminderEvents dedup table (ANALYSIS-A5 §3.3).
        // DEFAULT 0 keeps RemindOnDose = false for every pre-A5 row;
        // no reminder is ever "armed" by the upgrade.
        await AddColumnIfMissingAsync(
            table: "Medicines",
            column: "RemindOnDose",
            typeSpec: "INTEGER NOT NULL DEFAULT 0",
            cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""DoseReminderEvents"" (
                ""Id""         TEXT    NOT NULL CONSTRAINT ""PK_DoseReminderEvents"" PRIMARY KEY,
                ""MedicineId"" TEXT    NOT NULL,
                ""SlotKey""    TEXT    NOT NULL,
                ""LocalDate""  TEXT    NOT NULL,
                ""FiredAt""    INTEGER NOT NULL,
                ""Channel""    INTEGER NOT NULL
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_DoseReminderEvents_Dedup""
                ON ""DoseReminderEvents"" (""MedicineId"", ""SlotKey"", ""LocalDate"");",
            cancellationToken);
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
            // PRAGMA table_info returns columns:
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
