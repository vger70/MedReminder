namespace MedReminder.Application.Abstractions;

// Lets Infrastructure run code under the process-wide WriteGate, which
// is internal to the Application layer.
//
// Two kinds of callers (ANALYSIS-CATALOGUE-REMOTE-FEED.md §4.5, §11.5):
// - the paths that move a new file over a profile database (backup
//   restore, archive import, sync join / rebuild / rekey);
// - the reference-catalogue importer, for every database access it
//   makes (version read and replace transaction), whoever calls it:
//   the embedded import at boot or the remote feed refresh.
// A swap therefore never moves the file while the importer holds it
// open in a long write transaction, which fails on Windows.
//
// Not reentrant, like WriteGate: the action must not call a gated use
// case or another gated entry point.
public interface IDatabaseExclusiveAccess
{
    Task RunExclusiveAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);

    Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
