namespace MedReminder.Application.Sync;

// Process-wide signal that this device recorded operations (B.1 Phase
// 3d): the desktop sync service runs shortly after local writes instead
// of waiting for its interval (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §5.2, "debounced after local writes"). Registered as a singleton.
public sealed class SyncActivity
{
    public event EventHandler? LocalOperationsRecorded;

    internal void RaiseLocalOperationsRecorded() => LocalOperationsRecorded?.Invoke(this, EventArgs.Empty);
}
