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

    // The group key was changed on another device (Phase 4c): sync
    // resumes after the new passphrase or a pairing code (admin action).
    public bool NeedsNewKey { get; private set; }

    // The cloud session ended (Phase 4a): sync resumes after an
    // interactive sign-in from the sync window.
    public bool NeedsSignIn { get; private set; }

    public event EventHandler? Changed;

    public event EventHandler? RemoteChangesApplied;

    public void Report(DateTimeOffset at, SyncRunResult result)
    {
        lock (_lock)
        {
            LastRunAt = at;
            LastResult = result;
            LastError = null;
            NeedsSignIn = false;
            NeedsNewKey = result.NewKeyRequired;
            NeedsRebuild = (result.NewerGeneration is not null && !result.NewKeyRequired) || result.RebuildRequired;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (result.OperationsApplied > 0) RemoteChangesApplied?.Invoke(this, EventArgs.Empty);
    }

    public void ReportError(DateTimeOffset at, string message, bool needsSignIn = false)
    {
        lock (_lock)
        {
            LastRunAt = at;
            LastError = message;
            NeedsSignIn = needsSignIn;
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
            NeedsNewKey = false;
            NeedsSignIn = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
