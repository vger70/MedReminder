namespace MedReminder.Application.UpdateChecking;

// Orders the remote catalogue refresh after the passive application
// update check (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md §4.1).
// MainForm marks it when its startup check ends, whatever the outcome,
// including when the check is disabled. The catalogue refresh waits
// for it with a timeout, so a window that never loads cannot block the
// refresh forever. Singleton.
public sealed class StartupUpdateCheckSignal
{
    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsCompleted => _completed.Task.IsCompleted;

    public void MarkCompleted() => _completed.TrySetResult();

    // Completes when the signal is marked or the timeout elapses;
    // throws only when `cancellationToken` is cancelled.
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await _completed.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Proceed without the signal.
        }
    }
}
