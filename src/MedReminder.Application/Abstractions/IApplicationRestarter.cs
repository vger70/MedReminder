namespace MedReminder.Application.Abstractions;

// Self-orchestrated restart of the app: used after a backup restore
// to release the SQLite locks on the new DB and reload the UI with a
// consistent state, and after a profile switch to reboot the app
// against the newly selected profile.
//
// The UI implementation spawns a new process of the current
// executable and closes the current one. The
// Local\MedReminder.SingleInstance mutex is released by the exiting
// process in time for the new one.
//
// Callers that need to steer the new process — for example, to pass
// "--profile <id>" after a profile switch so the picker does not
// reappear (docs/ANALYSIS-MULTI-USER.md §6.1) — use the overload
// that accepts extra command-line arguments. The arguments are
// passed via ProcessStartInfo.ArgumentList to avoid shell-quoting
// concerns.
public interface IApplicationRestarter
{
    void RestartAndExit();

    void RestartAndExit(IReadOnlyList<string>? extraArgs);
}
