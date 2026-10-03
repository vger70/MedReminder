using System.Data.Common;
using System.Globalization;
using MedReminder.Application.Migrations;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Catalogue;
using MedReminder.Infrastructure.Persistence.Repositories;
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
    private readonly TimeProvider _clock;

    public DatabaseInitializer(
        MedReminderDbContext db,
        ILogger<DatabaseInitializer> log,
        TimeProvider? clock = null)
    {
        _db = db;
        _log = log;
        _clock = clock ?? TimeProvider.System;
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

        await ApplyLedgerFactsPatchAsync(cancellationToken);
        await ApplyLedgerDerivationPatchAsync(cancellationToken);
        await ApplyFactRetractionPatchAsync(cancellationToken);

        // Household step H1: low-stock emails sent by any device of the
        // sync group (replicated; SentEmailNotificationConfiguration).
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""SentEmailNotifications"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SentEmailNotifications"" PRIMARY KEY,
                ""MedicineId"" TEXT NOT NULL,
                ""StockEpoch"" INTEGER NOT NULL,
                ""EpochFactId"" TEXT NULL,
                ""SentAt"" INTEGER NOT NULL,
                CONSTRAINT ""FK_SentEmailNotifications_Medicines_MedicineId""
                    FOREIGN KEY (""MedicineId"") REFERENCES ""Medicines"" (""Id"") ON DELETE RESTRICT
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_SentEmailNotifications_MedicineId_SentAt""
                ON ""SentEmailNotifications"" (""MedicineId"", ""SentAt"");", cancellationToken);

        // Second low-stock warning (docs/notes/EVOLUTION-PROPOSALS-2.md
        // §3.1): the warning stage of every event and sent email. Rows
        // written before it are first-stage warnings.
        await AddColumnIfMissingAsync("NotificationEvents", "Stage", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await AddColumnIfMissingAsync("SentEmailNotifications", "Stage", "INTEGER NOT NULL DEFAULT 1", cancellationToken);

        // Prescription lifecycle (docs/notes/EVOLUTION-PROPOSALS-2.md
        // §3.2): the prescriptions (replicated) and the reminders to
        // collect them this device showed (not replicated).
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""Prescriptions"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Prescriptions"" PRIMARY KEY,
                ""MedicineId"" TEXT NOT NULL,
                ""RequestedOn"" TEXT NULL,
                ""IssuedOn"" TEXT NULL,
                ""Code"" TEXT NULL,
                ""Packages"" INTEGER NULL,
                ""ValidUntil"" TEXT NULL,
                ""CollectedOn"" TEXT NULL,
                ""RecordedAt"" INTEGER NOT NULL,
                ""UpdatedAt"" INTEGER NOT NULL,
                CONSTRAINT ""FK_Prescriptions_Medicines_MedicineId""
                    FOREIGN KEY (""MedicineId"") REFERENCES ""Medicines"" (""Id"") ON DELETE RESTRICT
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_Prescriptions_MedicineId""
                ON ""Prescriptions"" (""MedicineId"");", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""PrescriptionReminderEvents"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_PrescriptionReminderEvents"" PRIMARY KEY,
                ""PrescriptionId"" TEXT NOT NULL,
                ""MedicineId"" TEXT NOT NULL,
                ""ValidUntil"" TEXT NOT NULL,
                ""FiredAt"" INTEGER NOT NULL
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PrescriptionReminderEvents_PrescriptionId_ValidUntil""
                ON ""PrescriptionReminderEvents"" (""PrescriptionId"", ""ValidUntil"");", cancellationToken);

        // Administrative deadlines (docs/notes/EVOLUTION-PROPOSALS-2.md
        // §3.6): the deadlines (replicated) and the reminders this device
        // showed (not replicated).
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""Deadlines"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Deadlines"" PRIMARY KEY,
                ""MedicineId"" TEXT NULL,
                ""Kind"" INTEGER NOT NULL,
                ""Label"" TEXT NULL,
                ""DueOn"" TEXT NOT NULL,
                ""LeadDays"" INTEGER NOT NULL,
                ""RepeatMonths"" INTEGER NULL,
                ""Channels"" INTEGER NOT NULL,
                ""DoneOn"" TEXT NULL,
                ""RecordedAt"" INTEGER NOT NULL,
                ""UpdatedAt"" INTEGER NOT NULL,
                CONSTRAINT ""FK_Deadlines_Medicines_MedicineId""
                    FOREIGN KEY (""MedicineId"") REFERENCES ""Medicines"" (""Id"") ON DELETE RESTRICT
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_Deadlines_MedicineId""
                ON ""Deadlines"" (""MedicineId"");", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""DeadlineReminderEvents"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_DeadlineReminderEvents"" PRIMARY KEY,
                ""DeadlineId"" TEXT NOT NULL,
                ""MedicineId"" TEXT NULL,
                ""DueOn"" TEXT NOT NULL,
                ""FiredAt"" INTEGER NOT NULL
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_DeadlineReminderEvents_DeadlineId_DueOn""
                ON ""DeadlineReminderEvents"" (""DeadlineId"", ""DueOn"");", cancellationToken);

        // Shortage notices this device showed (docs/notes/
        // EVOLUTION-PROPOSALS-2.md §3.3; not replicated).
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""ShortageNoticeEvents"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_ShortageNoticeEvents"" PRIMARY KEY,
                ""MedicineId"" TEXT NOT NULL,
                ""Code"" TEXT NOT NULL,
                ""Start"" TEXT NOT NULL,
                ""FiredAt"" INTEGER NOT NULL
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ShortageNoticeEvents_MedicineId_Code_Start""
                ON ""ShortageNoticeEvents"" (""MedicineId"", ""Code"", ""Start"");", cancellationToken);
        await ExecuteRawSqlAsync(SyncOperationsTableSql, cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_SyncOperations_HlcPhysicalMs_HlcCounter""
                ON ""SyncOperations"" (""HlcPhysicalMs"", ""HlcCounter"");", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_SyncOperations_SegmentSeq""
                ON ""SyncOperations"" (""SegmentSeq"");", cancellationToken);

        // B.1 Phase 3c: sync progress per device of the group.
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""SyncPeers"" (
                ""DeviceId"" TEXT NOT NULL CONSTRAINT ""PK_SyncPeers"" PRIMARY KEY,
                ""Generation"" INTEGER NOT NULL,
                ""Seq"" INTEGER NOT NULL,
                ""Checkpoints"" INTEGER NOT NULL,
                ""CheckpointOperations"" INTEGER NOT NULL
            );", cancellationToken);

        // B.1 Phase 3b-2: the row an operation writes, for the HLC of
        // facts, and the per-medicine lookup of the count re-evaluation.
        await AddColumnIfMissingAsync("SyncOperations", "EntityId", "TEXT NULL", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_SyncOperations_MedicineId""
                ON ""SyncOperations"" (""MedicineId"");", cancellationToken);

        // B.1 Phase 3b: register versions and the conflict list.
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""SyncFieldVersions"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SyncFieldVersions"" PRIMARY KEY,
                ""MedicineId"" TEXT NOT NULL,
                ""EntityId"" TEXT NOT NULL,
                ""Register"" TEXT NOT NULL,
                ""HlcPhysicalMs"" INTEGER NOT NULL,
                ""HlcCounter"" INTEGER NOT NULL,
                ""DeviceId"" TEXT NOT NULL,
                ""Value"" TEXT NULL,
                ""BasePhysicalMs"" INTEGER NULL,
                ""BaseCounter"" INTEGER NULL,
                ""BaseDeviceId"" TEXT NULL
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_SyncFieldVersions_EntityId""
                ON ""SyncFieldVersions"" (""EntityId"");", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""SyncConflicts"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SyncConflicts"" PRIMARY KEY,
                ""Kind"" INTEGER NOT NULL,
                ""MedicineId"" TEXT NOT NULL,
                ""SubjectId"" TEXT NOT NULL,
                ""Register"" TEXT NULL,
                ""WinningValue"" TEXT NULL,
                ""LosingValue"" TEXT NULL,
                ""WinningDeviceId"" TEXT NULL,
                ""LosingDeviceId"" TEXT NULL,
                ""OtherId"" TEXT NULL,
                ""DetectedAt"" INTEGER NOT NULL
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_SyncConflicts_SubjectId""
                ON ""SyncConflicts"" (""SubjectId"");", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_SyncConflicts_MedicineId""
                ON ""SyncConflicts"" (""MedicineId"");", cancellationToken);

        // As-needed slots and extra intakes (docs/analysis/
        // ANALYSIS-INTRADAY-CONSUMPTION.md §5). Rows written before read
        // false: no past day changes. The slots of the upgraded database
        // are then corrected from today by AsNeededSlotBackfill.
        await ExecuteRawSqlAsync(PendingDataMigrations.CreateTableSql, cancellationToken);
        if (await AddColumnIfMissingAsync(
                "MedicationAdministrationSlots", "IsAsNeeded", "INTEGER NOT NULL DEFAULT 0", cancellationToken))
        {
            await _db.Database.ExecuteSqlRawAsync(
                PendingDataMigrations.MarkPendingSql, [AsNeededSlotBackfill.MigrationName], cancellationToken);
        }
        await AddColumnIfMissingAsync(
            "MedicationIntakes", "IsExtra", "INTEGER NOT NULL DEFAULT 0", cancellationToken);

        // Time-of-day presets (ANALYSIS-INTRADAY-CONSUMPTION.md §6),
        // device-local. Existing slots get their preset from their
        // description once (SlotPresetBackfill).
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""DoseTimePresets"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_DoseTimePresets"" PRIMARY KEY,
                ""BuiltInKey"" TEXT NULL,
                ""Label"" TEXT NULL,
                ""Time"" TEXT NULL,
                ""IsAsNeeded"" INTEGER NOT NULL,
                ""Order"" INTEGER NOT NULL,
                ""IsHidden"" INTEGER NOT NULL
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""DoseTimeDefaults"" (
                ""AdministrationsPerDay"" INTEGER NOT NULL CONSTRAINT ""PK_DoseTimeDefaults"" PRIMARY KEY,
                ""Times"" TEXT NOT NULL
            );", cancellationToken);
        if (await AddColumnIfMissingAsync("MedicationAdministrationSlots", "PresetId", "TEXT NULL", cancellationToken))
        {
            await _db.Database.ExecuteSqlRawAsync(
                PendingDataMigrations.MarkPendingSql, [SlotPresetBackfill.MigrationName], cancellationToken);
        }

        // Packages and their expiry (docs/analysis/
        // ANALYSIS-PACKAGE-EXPIRY.md §6), replicated.
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""StockPackages"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_StockPackages"" PRIMARY KEY,
                ""MedicineId"" TEXT NOT NULL,
                ""MovementId"" TEXT NULL,
                ""Quantity"" TEXT NOT NULL,
                ""ExpiresOn"" TEXT NULL,
                ""UseWithinDays"" INTEGER NULL,
                ""OpenedOn"" TEXT NULL,
                ""Batch"" TEXT NULL,
                ""ClosedOn"" TEXT NULL,
                ""Closure"" INTEGER NULL,
                ""RecordedAt"" INTEGER NOT NULL,
                ""UpdatedAt"" INTEGER NOT NULL,
                CONSTRAINT ""FK_StockPackages_Medicines_MedicineId""
                    FOREIGN KEY (""MedicineId"") REFERENCES ""Medicines"" (""Id"") ON DELETE RESTRICT
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE INDEX IF NOT EXISTS ""IX_StockPackages_MedicineId""
                ON ""StockPackages"" (""MedicineId"");", cancellationToken);
        // Package expiry notices this device showed (not replicated).
        await ExecuteRawSqlAsync(@"
            CREATE TABLE IF NOT EXISTS ""PackageExpiryNoticeEvents"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_PackageExpiryNoticeEvents"" PRIMARY KEY,
                ""PackageId"" TEXT NOT NULL,
                ""MedicineId"" TEXT NOT NULL,
                ""EffectiveExpiry"" TEXT NOT NULL,
                ""Stage"" INTEGER NOT NULL,
                ""FiredAt"" INTEGER NOT NULL
            );", cancellationToken);
        await ExecuteRawSqlAsync(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PackageExpiryNoticeEvents_PackageId_EffectiveExpiry_Stage""
                ON ""PackageExpiryNoticeEvents"" (""PackageId"", ""EffectiveExpiry"", ""Stage"");", cancellationToken);
    }

    // B.1 Phase 3a (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §7.3): the
    // local operation log. Empty until sync is enabled.
    private const string SyncOperationsTableSql = @"
        CREATE TABLE IF NOT EXISTS ""SyncOperations"" (
            ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SyncOperations"" PRIMARY KEY,
            ""HlcPhysicalMs"" INTEGER NOT NULL,
            ""HlcCounter"" INTEGER NOT NULL,
            ""DeviceId"" TEXT NOT NULL,
            ""Generation"" INTEGER NOT NULL,
            ""Type"" TEXT NOT NULL,
            ""SchemaVersion"" INTEGER NOT NULL,
            ""MedicineId"" TEXT NOT NULL,
            ""EntityId"" TEXT NULL,
            ""Payload"" TEXT NOT NULL,
            ""SegmentSeq"" INTEGER NULL
        );";

    // B.1 Phase 2d (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2, §4.4):
    // fact retraction and epoch identity. Additive only, one
    // transaction.
    //
    //   - FactRetractions: tombstones of retracted facts.
    //   - MedicationSuspensions.RecordedAt: decides whether a suspension
    //     can be retracted; older rows read as MinValue (Legacy after a
    //     freeze).
    //   - Medicines.StockEpochFactId, NotificationEvents.EpochFactId:
    //     low-stock dedup keyed on the fact that opened the epoch. Filled
    //     by the first derivation after the upgrade (LedgerSynchronizer).
    private async Task ApplyFactRetractionPatchAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, @"
            CREATE TABLE IF NOT EXISTS ""FactRetractions"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_FactRetractions"" PRIMARY KEY,
                ""MedicineId"" TEXT NOT NULL,
                ""FactId"" TEXT NOT NULL,
                ""Kind"" INTEGER NOT NULL,
                ""RecordedAt"" INTEGER NOT NULL,
                CONSTRAINT ""FK_FactRetractions_Medicines_MedicineId""
                    FOREIGN KEY (""MedicineId"") REFERENCES ""Medicines"" (""Id"") ON DELETE RESTRICT
            );", cancellationToken);
        await ExecuteAsync(connection, transaction, @"
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_FactRetractions_FactId""
                ON ""FactRetractions"" (""FactId"");", cancellationToken);
        await ExecuteAsync(connection, transaction, @"
            CREATE INDEX IF NOT EXISTS ""IX_FactRetractions_MedicineId""
                ON ""FactRetractions"" (""MedicineId"");", cancellationToken);

        foreach (var (table, column, typeSpec) in new[]
        {
            ("MedicationSuspensions", "RecordedAt", "INTEGER NOT NULL DEFAULT 0"),
            ("Medicines", "StockEpochFactId", "TEXT NULL"),
            ("NotificationEvents", "EpochFactId", "TEXT NULL"),
        })
        {
            await AddColumnIfMissingAsync(connection, transaction, table, column, typeSpec, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // B.1 Phase 2c-2 (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2,
    // §4.3, §13): the facts the ledger derivation reads, then a
    // re-freeze. One transaction.
    //
    //   - MedicineActivityChanges: dated activation history (D15).
    //   - StockCounts outcome columns and Notes: the count outcome is
    //     stored with the fact until Phase 3.
    //   - MedicationScheduleHistories.RecordedAt: same-date rows resolve
    //     to the later recorded one (§17).
    //   - Medicines.LedgerBaselineEpoch.
    //   - MedicationIntakes.RecordedAt, the guard: when it is missing the
    //     database predates 2c-2, so it is re-frozen (LedgerFreeze).
    //     Rows written between the 2b patch and now include count
    //     corrections without a count fact; freezing them keeps every
    //     past number.
    private async Task ApplyLedgerDerivationPatchAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, @"
            CREATE TABLE IF NOT EXISTS ""MedicineActivityChanges"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MedicineActivityChanges"" PRIMARY KEY,
                ""MedicineId"" TEXT NOT NULL,
                ""Day"" TEXT NOT NULL,
                ""Active"" INTEGER NOT NULL,
                ""RecordedAt"" INTEGER NOT NULL,
                CONSTRAINT ""FK_MedicineActivityChanges_Medicines_MedicineId""
                    FOREIGN KEY (""MedicineId"") REFERENCES ""Medicines"" (""Id"") ON DELETE RESTRICT
            );", cancellationToken);
        await ExecuteAsync(connection, transaction, @"
            CREATE INDEX IF NOT EXISTS ""IX_MedicineActivityChanges_MedicineId_RecordedAt""
                ON ""MedicineActivityChanges"" (""MedicineId"", ""RecordedAt"");", cancellationToken);

        foreach (var (table, column, typeSpec) in new[]
        {
            ("StockCounts", "Notes", "TEXT NULL"),
            ("StockCounts", "LedgerAtStartOfDay", "TEXT NOT NULL DEFAULT '0'"),
            ("StockCounts", "CountDayScheduled", "TEXT NOT NULL DEFAULT '0'"),
            ("StockCounts", "Correction", "TEXT NOT NULL DEFAULT '0'"),
            ("StockCounts", "MaterializesCountDay", "INTEGER NOT NULL DEFAULT 0"),
            ("StockCounts", "AdvancesEpoch", "INTEGER NOT NULL DEFAULT 0"),
            ("MedicationScheduleHistories", "RecordedAt", "INTEGER NOT NULL DEFAULT 0"),
            ("Medicines", "LedgerBaselineEpoch", "INTEGER NOT NULL DEFAULT 1"),
        })
        {
            await AddColumnIfMissingAsync(connection, transaction, table, column, typeSpec, cancellationToken);
        }

        if (await AddColumnIfMissingAsync(connection, transaction,
                "MedicationIntakes", "RecordedAt", "INTEGER NOT NULL DEFAULT 0", cancellationToken))
        {
            await LedgerFreeze.ApplyAsync(
                connection, transaction, _clock.GetUtcNow(), _clock.LocalTimeZone, cancellationToken);
            _log.LogInformation("Ledger re-frozen for the ledger derivation.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> AddColumnIfMissingAsync(
        DbConnection connection, DbTransaction transaction,
        string table, string column, string typeSpec, CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(connection, transaction, table, column, cancellationToken))
        {
            return false;
        }
        await ExecuteAsync(connection, transaction,
            $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {typeSpec};", cancellationToken);
        _log.LogInformation("Added missing column {Column} to {Table}.", column, table);
        return true;
    }

    // B.1 Phase 2b (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §3.5, §4.2,
    // §7.3): facts / derived split of the stock ledger and dated slot
    // history. One transaction, so a crash cannot leave the movements
    // marked Legacy without the cutoff, or slots without their set.
    //
    //   - StockCounts, LedgerCutoff, MedicationAdministrationSlotSets:
    //     CREATE TABLE IF NOT EXISTS, same DDL as EnsureCreated.
    //   - StockMovements.Origin: every row present before the patch
    //     becomes Legacy (DEFAULT 1) and the cutoff is stored as the
    //     day before the patch. Guarded by the column check, so it runs
    //     once per database.
    //   - MedicationAdministrationSlots.SetId: the slots present before
    //     the patch become one set per medicine, in force from the
    //     medicine's StartDate. The set reuses the medicine's Id, which
    //     keeps the backfill in SQL and the Guid text format identical.
    private async Task ApplyLedgerFactsPatchAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var now = _clock.GetUtcNow();
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, _clock.LocalTimeZone).DateTime);
        var cutoffDay = localToday.AddDays(-1);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, @"
            CREATE TABLE IF NOT EXISTS ""StockCounts"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_StockCounts"" PRIMARY KEY,
                ""MedicineId"" TEXT NOT NULL,
                ""CountDay"" TEXT NOT NULL,
                ""CountedQuantity"" TEXT NOT NULL,
                ""TakenToday"" TEXT NOT NULL,
                ""ThresholdAtCount"" INTEGER NOT NULL,
                ""RecordedAt"" INTEGER NOT NULL,
                CONSTRAINT ""FK_StockCounts_Medicines_MedicineId""
                    FOREIGN KEY (""MedicineId"") REFERENCES ""Medicines"" (""Id"") ON DELETE RESTRICT
            );", cancellationToken);
        await ExecuteAsync(connection, transaction, @"
            CREATE INDEX IF NOT EXISTS ""IX_StockCounts_MedicineId""
                ON ""StockCounts"" (""MedicineId"");", cancellationToken);

        await ExecuteAsync(connection, transaction, @"
            CREATE TABLE IF NOT EXISTS ""LedgerCutoff"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LedgerCutoff"" PRIMARY KEY,
                ""CutoffDay"" TEXT NOT NULL,
                ""FrozenAt"" INTEGER NOT NULL
            );", cancellationToken);

        await ExecuteAsync(connection, transaction, @"
            CREATE TABLE IF NOT EXISTS ""MedicationAdministrationSlotSets"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MedicationAdministrationSlotSets"" PRIMARY KEY,
                ""MedicineId"" TEXT NOT NULL,
                ""EffectiveFrom"" TEXT NOT NULL,
                ""RecordedAt"" INTEGER NOT NULL,
                CONSTRAINT ""FK_MedicationAdministrationSlotSets_Medicines_MedicineId""
                    FOREIGN KEY (""MedicineId"") REFERENCES ""Medicines"" (""Id"") ON DELETE RESTRICT
            );", cancellationToken);
        await ExecuteAsync(connection, transaction, @"
            CREATE INDEX IF NOT EXISTS ""IX_MedicationAdministrationSlotSets_MedicineId_RecordedAt""
                ON ""MedicationAdministrationSlotSets"" (""MedicineId"", ""RecordedAt"");", cancellationToken);

        if (!await ColumnExistsAsync(connection, transaction, "StockMovements", "Origin", cancellationToken))
        {
            await ExecuteAsync(connection, transaction,
                $@"ALTER TABLE ""StockMovements"" ADD COLUMN ""Origin"" INTEGER NOT NULL DEFAULT {(int)StockMovementOrigin.Legacy};",
                cancellationToken);
            await ExecuteAsync(connection, transaction, @"
                INSERT OR IGNORE INTO ""LedgerCutoff"" (""Id"", ""CutoffDay"", ""FrozenAt"")
                VALUES ($id, $cutoffDay, $frozenAt);",
                cancellationToken,
                ("$id", LedgerCutoff.SingletonId),
                ("$cutoffDay", cutoffDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$frozenAt", now.UtcTicks));
            _log.LogInformation(
                "Stock movements frozen as Legacy; ledger cutoff set to {CutoffDay}.", cutoffDay);
        }

        if (!await ColumnExistsAsync(connection, transaction, "MedicationAdministrationSlots", "SetId", cancellationToken))
        {
            await ExecuteAsync(connection, transaction,
                @"ALTER TABLE ""MedicationAdministrationSlots"" ADD COLUMN ""SetId"" TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';",
                cancellationToken);
            await ExecuteAsync(connection, transaction, @"
                INSERT INTO ""MedicationAdministrationSlotSets"" (""Id"", ""MedicineId"", ""EffectiveFrom"", ""RecordedAt"")
                SELECT m.""Id"", m.""Id"", m.""StartDate"", $recordedAt
                FROM ""Medicines"" m
                WHERE EXISTS (SELECT 1 FROM ""MedicationAdministrationSlots"" s WHERE s.""MedicineId"" = m.""Id"");",
                cancellationToken,
                ("$recordedAt", now.UtcTicks));
            await ExecuteAsync(connection, transaction,
                @"UPDATE ""MedicationAdministrationSlots"" SET ""SetId"" = ""MedicineId"";",
                cancellationToken);
            _log.LogInformation("Administration slots grouped into dated slot sets.");
        }
        await ExecuteAsync(connection, transaction, @"
            CREATE INDEX IF NOT EXISTS ""IX_MedicationAdministrationSlots_SetId""
                ON ""MedicationAdministrationSlots"" (""SetId"");", cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
        => LedgerFreeze.ExecuteAsync(connection, transaction, sql, cancellationToken, parameters);

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
        => await ColumnExistsAsync(connection, transaction: null, table, column, cancellationToken);

    private static async Task<bool> ColumnExistsAsync(
        DbConnection connection, DbTransaction? transaction, string table, string column,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
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
