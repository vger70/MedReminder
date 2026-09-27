using MedReminder.Application.Export;
using MedReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
        if (payload.LedgerCutoff is not null)
        {
            db.LedgerCutoffs.Add(ExportMapper.ToEntity(payload.LedgerCutoff));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
