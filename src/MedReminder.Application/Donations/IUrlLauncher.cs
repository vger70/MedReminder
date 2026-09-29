namespace MedReminder.Application.Donations;

// One-method port wrapping the browser launch (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §3.1). Abstracting the launch
// makes the "successful launch" / "launch failure" paths unit-testable
// without spawning a real browser — the only production implementation
// (ShellUrlLauncher) is the single place Process.Start is called.
public interface IUrlLauncher
{
    // Hands the HTTPS URL to the default browser. Returns true on
    // success; on failure returns false and sets error to the
    // exception message (logged, never shown raw to the user).
    bool TryLaunch(Uri httpsUrl, out string? error);
}
