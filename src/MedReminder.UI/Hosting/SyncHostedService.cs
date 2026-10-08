using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Application.Sync;
using MedReminder.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Hosting;

// Runs the sync engine (B.1 Phase 3d, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §7.4): shortly after start, every
// Sync:IntervalMinutes (default 5), and 10 seconds after this device
// records operations. Idle while sync is not enabled for the profile.
//
// A pending reset (an import or a restore replaced the database) is
// handled first: the device starts a new generation before any other
// run. Each run has its own DI scope, like the other hosted services.
// Registered as a singleton so the sync window can run it at once, and
// suspend it before an import or a restore.
internal sealed class SyncHostedService : BackgroundService
{
    private const int DefaultIntervalMinutes = 5;
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(10);

    private readonly IServiceProvider _services;
    private readonly ISyncSettingsStore _settings;
    private readonly SyncActivity _activity;
    private readonly SyncStatus _status;
    private readonly TimeProvider _clock;
    private readonly ILogger<SyncHostedService> _log;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _run = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly System.Threading.Timer _debounce;
    private volatile bool _suspended;

    public SyncHostedService(
        IServiceProvider services,
        ISyncSettingsStore settings,
        SyncActivity activity,
        SyncStatus status,
        TimeProvider clock,
        IConfiguration configuration,
        ILogger<SyncHostedService> log)
    {
        _services = services;
        _settings = settings;
        _activity = activity;
        _status = status;
        _clock = clock;
        _log = log;
        var configured = int.TryParse(configuration["Sync:IntervalMinutes"], out var v) ? v : DefaultIntervalMinutes;
        _interval = TimeSpan.FromMinutes(Math.Max(1, configured));
        _debounce = new System.Threading.Timer(_ => _wake.Release(), null, Timeout.Infinite, Timeout.Infinite);
        _activity.LocalOperationsRecorded += OnLocalOperations;
    }

    public bool IsEnabled => _settings.Load() is not null;

    // Runs now and returns the result; null when sync is off, suspended
    // or the run failed (see SyncStatus).
    // Off the UI thread: the window awaits it without freezing.
    public Task<SyncRunResult?> RunNowAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => RunOnceAsync(cancellationToken), cancellationToken);

    // Before an import or a restore: publishes what is pending, then
    // stops running until the application restarts (§5.7).
    public async Task SuspendAsync(CancellationToken cancellationToken = default)
    {
        await RunNowAsync(cancellationToken);
        await _run.WaitAsync(cancellationToken);
        try
        {
            _suspended = true;
        }
        finally
        {
            _run.Release();
        }
    }

    // Runs an action while no sync run is in progress (Phase 4c): a key
    // rotation or a rekey must not overlap a run that still uses the old
    // key and generation.
    public async Task<T> WhileIdleAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _run.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            _run.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, _clock, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await _wake.WaitAsync(_interval, stoppingToken);
                // Several wake-ups while a run was going on need one run.
                while (_wake.CurrentCount > 0) await _wake.WaitAsync(0, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Clean shutdown.
        }
    }

    public override void Dispose()
    {
        _activity.LocalOperationsRecorded -= OnLocalOperations;
        _debounce.Dispose();
        base.Dispose();
    }

    private void OnLocalOperations(object? sender, EventArgs e)
    {
        if (!_suspended) _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    private async Task<SyncRunResult?> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (_suspended || _settings.Load() is not { } settings) return null;

        await _run.WaitAsync(cancellationToken);
        try
        {
            if (_suspended) return null;
            await using var scope = _services.CreateAsyncScope();
            if (settings.ResetPending)
            {
                var reset = await scope.ServiceProvider.GetRequiredService<ResetSyncGeneration>()
                    .ExecuteAsync(scope.ServiceProvider.GetRequiredService<ISyncTransport>(), cancellationToken);
                _log.LogInformation("Sync: started generation {Generation} after an import or a restore.", reset.Generation);
            }

            // Household step H3c: an adopted profile group records the
            // household that claims it, before the run publishes.
            if (await scope.ServiceProvider.GetRequiredService<HouseholdLinks>().RecordAsync(cancellationToken))
                _log.LogInformation("Sync: this profile group was linked to the household.");

            var result = await scope.ServiceProvider.GetRequiredService<SyncEngine>().RunAsync(cancellationToken);
            _status.Report(_clock.GetUtcNow(), result);
            _log.LogInformation(
                "Sync run: published={Published}, segments applied={Segments}, operations applied={Applied}, " +
                "checkpoint={Checkpoint}, segments deleted={Deleted}, problems={Problems}",
                result.OperationsPublished, result.SegmentsApplied, result.OperationsApplied,
                result.CheckpointWritten, result.SegmentsDeleted, result.Problems.Count);
            foreach (var problem in result.Problems) _log.LogWarning("Sync: {Problem}", problem);
            if (result.NewKeyRequired)
                _log.LogWarning("Sync: the group key was changed on another device; this device needs the new key.");
            else if (result.NewerGeneration is { } generation)
                _log.LogWarning("Sync: generation {Generation} exists; this profile must be rebuilt.", generation);
            if (result.RebuildRequired)
                _log.LogWarning("Sync: segments needed by this device were deleted; this profile must be rebuilt.");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudSignInRequiredException ex)
        {
            // Runs keep failing until the user signs in again from the
            // sync window; nothing is lost meanwhile (operations stay in
            // the local log).
            _status.ReportError(_clock.GetUtcNow(), ex.Message, needsSignIn: true);
            _log.LogWarning("Sync run skipped: {Provider} needs a new sign-in.", ex.Provider);
            return null;
        }
        catch (Exception ex)
        {
            // The folder or the provider may be offline; the next run retries.
            _status.ReportError(_clock.GetUtcNow(), ex.Message);
            _log.LogWarning("Sync run failed: {ErrorMessage}", ex.Message);
            return null;
        }
        finally
        {
            _run.Release();
        }
    }
}
