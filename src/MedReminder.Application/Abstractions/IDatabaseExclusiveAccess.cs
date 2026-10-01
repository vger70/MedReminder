namespace MedReminder.Application.Abstractions;

// Lets Infrastructure run code under the process-wide WriteGate, which
// is internal to the Application layer.
//
// Used by the paths that move a new file over a profile database
// (backup restore, archive import, sync join / rebuild / rekey): they
// must not run while the remote catalogue import holds the database
// open in a long write transaction, or the file move fails on Windows
// with the handle still open. Since the remote catalogue refresh also
// runs during the session, not only at startup, the two can meet
// (ANALYSIS-CATALOGUE-REMOTE-FEED.md §4.5).
//
// Not reentrant, like WriteGate: the action must not call a gated use
// case.
public interface IDatabaseExclusiveAccess
{
    Task RunExclusiveAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
}
