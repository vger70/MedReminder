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
// right before the file moves, and returns how to undo itself: a step
// that must exist only if the swap happens (the sync reset marker) is
// never left behind by a cancelled wait for the gate, nor by a move that
// failed and was rolled back.
internal static class ProfileDatabaseSwap
{
    public static Task ReplaceAsync(
        IDatabaseExclusiveAccess access, MedReminderDbContext? db, string target, string newFile,
        TimeProvider clock, CancellationToken cancellationToken, Func<Action?>? beforeSwap = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        return access.RunExclusiveAsync(_ =>
        {
            var undo = beforeSwap?.Invoke();
            Replace(db, target, newFile, clock, undo);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    // db: the caller's open context, closed before the move; null when
    // the host holds no context of its own (the mobile import).
    // undoBeforeSwap: run when the swap did not happen and the current
    // database is in place again.
    private static void Replace(MedReminderDbContext? db, string target, string newFile, TimeProvider clock,
        Action? undoBeforeSwap)
    {
        string? backup = null;
        try
        {
            SqliteConnection.ClearAllPools();
            var connection = db?.Database.GetDbConnection();
            if (connection is not null && connection.State != ConnectionState.Closed)
            {
                connection.Close();
            }

            if (File.Exists(target))
            {
                var aside = $"{target}.bak-{clock.GetUtcNow():yyyyMMddHHmmss}";
                File.Move(target, aside, overwrite: false);
                backup = aside;
            }
        }
        catch
        {
            // Nothing moved: the current database is still in place.
            Undo(undoBeforeSwap);
            throw;
        }

        try
        {
            File.Move(newFile, target, overwrite: false);
        }
        catch
        {
            if (RollBack(target, backup)) Undo(undoBeforeSwap);
            throw;
        }

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var side = target + suffix;
            if (File.Exists(side))
            {
                try { File.Delete(side); } catch { /* best effort */ }
            }
        }
    }

    // After a failed move of the new file: whatever stands at the target
    // came from that move (a partial copy across volumes), since the
    // current file was moved aside first or did not exist; remove it and
    // put the current file back. False when that fails too: the database
    // then stays in the .bak file and the original error still surfaces.
    private static bool RollBack(string target, string? backup)
    {
        try
        {
            if (File.Exists(target)) File.Delete(target);
            if (backup is not null) File.Move(backup, target, overwrite: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Undo(Action? undo)
    {
        try { undo?.Invoke(); } catch { /* keep the first error */ }
    }
}
