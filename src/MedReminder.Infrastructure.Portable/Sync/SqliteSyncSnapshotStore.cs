using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedReminder.Infrastructure.Sync;

// Genesis and checkpoint images as SQLite database files (B.1 Phase 3c,
// docs/SYNC-FORMAT.md §5). The image carries the replicated state exactly
// as the database holds it: facts, frozen facts and cutoff, register
// versions, tombstones and the operation log (the HLC of every fact,
// read by the count re-evaluation). What is not replicated is removed:
//
//   - derived stock rows (every device derives them, §3.4);
//   - NotificationEvents, DoseReminderEvents, PrescriptionReminderEvents,
//     DeadlineReminderEvents, ShortageNoticeEvents, PackageExpiryNoticeEvents
//     (device-local, §4.2);
//   - the hint conflicts and SyncPeers (local notices, local progress).
//     Register conflicts stay: they follow from the register versions
//     and every device lists the same ones, a joining device included;
//   - the reference catalogue (shipped with the app; it is re-imported
//     because its version lives in the rows).
//
// Temporary files go to the profile folder, next to the database, never
// elsewhere (CLAUDE.md §5).
internal sealed class SqliteSyncSnapshotStore : ISyncSnapshotStore
{
    // Bump when a schema patch adds replicated data, so an older app
    // refuses an image it would read incompletely (R7).
    //   1: original image.
    //   2: the log may hold MedicineDeleted: a medicine absent from the
    //      image whose later operations must be skipped, which an app
    //      without that operation cannot do.
    //   3: SentEmailNotifications (household step H1), replicated: an
    //      older app would drop them and send those emails again.
    //   4: SentEmailNotifications.Stage (second low-stock warning): an
    //      older app would read a second-stage email as a first-stage one.
    //   5: Prescriptions (prescription lifecycle), replicated: an older
    //      app would drop them.
    //   6: Deadlines (administrative deadlines), replicated: an older app
    //      would drop them.
    //   7: MedicationAdministrationSlots.IsAsNeeded and
    //      MedicationIntakes.IsExtra (ANALYSIS-INTRADAY-CONSUMPTION.md §5):
    //      an older app would consume as-needed slots every day and read
    //      extra intakes as scheduled ones.
    //   8: StockPackages (package expiry), replicated: an older app would
    //      drop them.
    //   9: Prescriptions.Dispensations and PrescriptionDispensations
    //      (repeatable prescriptions), replicated: an older app would read
    //      a repeatable prescription as a single one and drop its
    //      dispensations.
    public const int CurrentSchemaVersion = 9;

    private static readonly string[] NotReplicated =
    [
        @"DELETE FROM ""StockMovements"" WHERE ""Origin"" = 3;",
        @"DELETE FROM ""NotificationEvents"";",
        @"DELETE FROM ""DoseReminderEvents"";",
        @"DELETE FROM ""PrescriptionReminderEvents"";",
        @"DELETE FROM ""DeadlineReminderEvents"";",
        @"DELETE FROM ""ShortageNoticeEvents"";",
        @"DELETE FROM ""PackageExpiryNoticeEvents"";",
        // Device-local time-of-day presets (DoseTimePreset).
        @"DELETE FROM ""DoseTimePresets"";",
        @"DELETE FROM ""DoseTimeDefaults"";",
        @"DELETE FROM ""SyncConflicts"" WHERE ""Kind"" IN (4, 5, 6);",
        @"DELETE FROM ""SyncPeers"";",
        @"DELETE FROM ""reference_medicine_ingredients"";",
        @"DELETE FROM ""reference_active_ingredients"";",
        @"DELETE FROM ""reference_medicines"";",
    ];

    private readonly MedReminderDbContext _db;
    private readonly TimeProvider _clock;

    public SqliteSyncSnapshotStore(MedReminderDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public int SchemaVersion => CurrentSchemaVersion;

    public async Task<byte[]> CaptureAsync(CancellationToken cancellationToken)
    {
        var connection = (SqliteConnection)_db.Database.GetDbConnection();
        var directory = Path.GetDirectoryName(Path.GetFullPath(connection.DataSource))!;
        var temp = Path.Combine(directory, $"sync-image-{Guid.NewGuid():N}.tmp");
        try
        {
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(cancellationToken);
            await using (var vacuum = connection.CreateCommand())
            {
                vacuum.CommandText = "VACUUM INTO $path;";
                vacuum.Parameters.AddWithValue("$path", temp);
                await vacuum.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var image = new SqliteConnection($"Data Source={temp};Pooling=False"))
            {
                await image.OpenAsync(cancellationToken);
                foreach (var sql in NotReplicated)
                {
                    await using var delete = image.CreateCommand();
                    delete.CommandText = sql;
                    await delete.ExecuteNonQueryAsync(cancellationToken);
                }
                await using var compact = image.CreateCommand();
                compact.CommandText = "PRAGMA journal_mode = DELETE; VACUUM;";
                await compact.ExecuteNonQueryAsync(cancellationToken);
            }
            return await File.ReadAllBytesAsync(temp, cancellationToken);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public async Task BuildDatabaseAsync(
        string databasePath,
        byte[] image,
        int generation,
        IReadOnlyDictionary<Guid, int> appliedVector,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(appliedVector);
        if (File.Exists(databasePath)) throw new IOException("The target database file already exists.");

        await File.WriteAllBytesAsync(databasePath, image, cancellationToken);

        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False;Foreign Keys=True")
            .Options;
        await using var db = new MedReminderDbContext(options);
        // An image from an older app is brought to the current schema by
        // the same boot patches as an existing profile.
        await new DatabaseInitializer(db, NullLogger<DatabaseInitializer>.Instance, _clock)
            .InitializeAsync(cancellationToken);

        foreach (var (device, seq) in appliedVector)
        {
            db.SyncPeers.Add(new Domain.Sync.SyncPeer { DeviceId = device, Generation = generation, Seq = seq });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
