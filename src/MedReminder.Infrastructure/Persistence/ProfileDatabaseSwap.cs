using System.Data;
using MedReminder.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Storage;

// Moves a new database file into a profile's database path (the import
// strategy of docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.2 steps 8-9,
// shared with the sync join and rebuild of B.1 Phase 3d): release the
// live connections, keep the current file as <db>.bak-<timestamp>, move
// the new file in, drop stale WAL / SHM side files. A restart follows so
// the hosted services open the new file cleanly. One process owns each
// profile database, so no other process holds it (CLAUDE.md §7).
internal static class ProfileDatabaseSwap
{
    public static void Replace(MedReminderDbContext db, string target, string newFile, TimeProvider clock)
    {
        SqliteConnection.ClearAllPools();
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Closed)
        {
            connection.Close();
        }

        if (File.Exists(target))
        {
            File.Move(target, $"{target}.bak-{clock.GetUtcNow():yyyyMMddHHmmss}", overwrite: false);
        }
        File.Move(newFile, target, overwrite: false);

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var side = target + suffix;
            if (File.Exists(side))
            {
                try { File.Delete(side); } catch { /* best effort */ }
            }
        }
    }
}
