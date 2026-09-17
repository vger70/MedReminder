namespace MedReminder.Application.Abstractions;

// Self-orchestrated restart of the app: used after a backup restore to
// release the SQLite locks on the new DB and reload the UI with a
// consistent state.
//
// The UI implementation spawns a new process of the current executable
// and closes the current one. The Local\MedReminder.SingleInstance
// mutex is released by the exiting process in time for the new one.
public interface IApplicationRestarter
{
    void RestartAndExit();
}
