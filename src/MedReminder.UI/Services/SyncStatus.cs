using MedReminder.Application.Sync;

namespace MedReminder.UI.Services;

// State of the desktop sync, shared by the hosted service and the sync
// window (B.1 Phase 3d). Singleton. Events are raised on the thread of
// the sync run; forms marshal them to the UI thread.
internal sealed class SyncStatus
{
    private readonly object _lock = new();

    public DateTimeOffset? LastRunAt { get; private set; }

    public SyncRunResult? LastResult { get; private set; }

    // Message of the last failed run (no content, no passphrase).
    public string? LastError { get; private set; }

    // A newer generation exists, or this device was away too long: the
    // profile must be rebuilt from the group (admin action).
    public bool NeedsRebuild { get; private set; }

    public event EventHandler? Changed;

    public event EventHandler? RemoteChangesApplied;

    public void Report(DateTimeOffset at, SyncRunResult result)
    {
        lock (_lock)
        {
            LastRunAt = at;
            LastResult = result;
            LastError = null;
            NeedsRebuild = result.NewerGeneration is not null || result.RebuildRequired;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (result.OperationsApplied > 0) RemoteChangesApplied?.Invoke(this, EventArgs.Empty);
    }

    public void ReportError(DateTimeOffset at, string message)
    {
        lock (_lock)
        {
            LastRunAt = at;
            LastError = message;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        lock (_lock)
        {
            LastRunAt = null;
            LastResult = null;
            LastError = null;
            NeedsRebuild = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
