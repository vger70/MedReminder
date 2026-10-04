using System.Data;
using MedReminder.Application.Abstractions;
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
//
// Every path that replaces a profile database goes through here: archive
// import, backup restore, sync join / rebuild / rekey.
//
// The swap runs under IDatabaseExclusiveAccess: the catalogue importer
// keeps the database open in a write transaction for several seconds and
// may run at any time of the session, so the swap waits for it instead
// of failing on the open handle. `beforeSwap` runs under the gate too,
// right before the file moves: a step that must happen only if the swap
// happens (the sync reset marker) is never left behind by a cancelled
// wait for the gate.
internal static class ProfileDatabaseSwap
{
    public static Task ReplaceAsync(
        IDatabaseExclusiveAccess access, MedReminderDbContext db, string target, string newFile,
        TimeProvider clock, CancellationToken cancellationToken, Action? beforeSwap = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        return access.RunExclusiveAsync(_ =>
        {
            beforeSwap?.Invoke();
            Replace(db, target, newFile, clock);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    private static void Replace(MedReminderDbContext db, string target, string newFile, TimeProvider clock)
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
