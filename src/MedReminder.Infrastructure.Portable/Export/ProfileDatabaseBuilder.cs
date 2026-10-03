using MedReminder.Application.Export;
using MedReminder.Application.Migrations;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedReminder.Infrastructure.Export;

// Materialises a decrypted archive payload into a new SQLite database
// file (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.2 step 9),
// extracted from ImportService so the Windows import and a mobile
// replica build the database the same way
// (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §13 Phase 1). The caller
// owns the target path and the swap into place.
internal static class ProfileDatabaseBuilder
{
    // clock: the import instant, used to map an older archive to the
    // current model (ExportPayloadUpgrader); System when null.
    public static async Task BuildAsync(
        string databasePath,
        ExportPayload payload,
        CancellationToken cancellationToken,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(payload);

        ExportPayloadUpgrader.UpgradeToCurrent(payload, clock ?? TimeProvider.System);

        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(SqliteConnectionStrings.ForFile(databasePath))
            .Options;

        await using var db = new MedReminderDbContext(options);

        // Fresh schema for the current model. The archive's schemaVersion
        // has already been checked as <= current; older archives simply
        // omit newer columns, which take their defaults (§4.3).
        await db.Database.EnsureCreatedAsync(cancellationToken);

        // The archive may come from a version before the as-needed flag:
        // its slots are corrected from today on first use
        // (AsNeededSlotBackfill, idempotent).
        await db.Database.ExecuteSqlRawAsync(PendingDataMigrations.CreateTableSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            PendingDataMigrations.MarkPendingSql, [AsNeededSlotBackfill.MigrationName], cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Parents first (Medicines), then dependents — every dependent
        // table FKs back to Medicines with ON DELETE RESTRICT.
        db.Medicines.AddRange(payload.Medicines.Select(ExportMapper.ToEntity));
        db.StockMovements.AddRange(payload.StockMovements.Select(ExportMapper.ToEntity));
        db.MedicationScheduleHistories.AddRange(
            payload.MedicationScheduleHistory.Select(ExportMapper.ToEntity));
        db.MedicationAdministrationSlotSets.AddRange(
            payload.MedicationAdministrationSlotSets.Select(ExportMapper.ToEntity));
        db.MedicationAdministrationSlots.AddRange(
            payload.MedicationAdministrationSlots.Select(ExportMapper.ToEntity));
        db.MedicationSuspensions.AddRange(
            payload.MedicationSuspensions.Select(ExportMapper.ToEntity));
        db.MedicationIntakes.AddRange(payload.MedicationIntakes.Select(ExportMapper.ToEntity));
        db.NotificationEvents.AddRange(payload.NotificationEvents.Select(ExportMapper.ToEntity));
        db.DoseReminderEvents.AddRange(payload.DoseReminderEvents.Select(ExportMapper.ToEntity));
        db.StockCounts.AddRange(payload.StockCounts.Select(ExportMapper.ToEntity));
        db.Prescriptions.AddRange((payload.Prescriptions ?? []).Select(ExportMapper.ToEntity));
        db.Deadlines.AddRange((payload.Deadlines ?? []).Select(ExportMapper.ToEntity));
        if (payload.LedgerCutoff is not null)
        {
            db.LedgerCutoffs.Add(ExportMapper.ToEntity(payload.LedgerCutoff));
        }

        await db.SaveChangesAsync(cancellationToken);

        // An imported profile starts frozen, as the boot patch leaves an
        // existing one (B.1 Phase 2c-2): the archive's numbers stay
        // exactly as exported and the derivation starts after the import.
        var importClock = clock ?? TimeProvider.System;
        await LedgerFreeze.ApplyAsync(
            db.Database.GetDbConnection(),
            transaction.GetDbTransaction(),
            importClock.GetUtcNow(),
            importClock.LocalTimeZone,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
