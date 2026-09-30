using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Hosting;

// Runs the household sync (household step H3d; docs/analysis/
// ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §5): shortly after start and every
// Household:IntervalMinutes (default 15). Idle while the household is not
// on a storage (HouseholdSync.RunAsync does nothing then). Each run has
// its own DI scope. A singleton, so the installation window can run it at
// once and read the outcome of the last run.
//
// Logs carry counters, ids and setting names only (HouseholdSyncResult).
internal sealed class HouseholdHostedService : BackgroundService
{
    private const int DefaultIntervalMinutes = 15;
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);

    private readonly IServiceProvider _services;
    private readonly TimeProvider _clock;
    private readonly ILogger<HouseholdHostedService> _log;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _run = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);

    public HouseholdHostedService(IServiceProvider services, TimeProvider clock, IConfiguration configuration,
        ILogger<HouseholdHostedService> log)
    {
        _services = services;
        _clock = clock;
        _log = log;
        var configured = int.TryParse(configuration["Household:IntervalMinutes"], out var v) ? v : DefaultIntervalMinutes;
        _interval = TimeSpan.FromMinutes(Math.Max(1, configured));
    }

    public DateTimeOffset? LastRunAt { get; private set; }

    public HouseholdSyncResult? LastResult { get; private set; }

    public string? LastError { get; private set; }

    public event EventHandler? Changed;

    // Off the UI thread; null when the run failed (LastError).
    public Task<HouseholdSyncResult?> RunNowAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => RunOnceAsync(cancellationToken), cancellationToken);

    // Runs an action while no household run is in progress: a publish or a
    // join replaces what a run reads.
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
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Clean shutdown.
        }
    }

    private async Task<HouseholdSyncResult?> RunOnceAsync(CancellationToken cancellationToken)
    {
        await _run.WaitAsync(cancellationToken);
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IHouseholdStore>();
            if ((await store.EnsureCreatedAsync(cancellationToken)).Storage is null) return null;

            var result = await scope.ServiceProvider.GetRequiredService<HouseholdSync>().RunAsync(cancellationToken);
            LastRunAt = _clock.GetUtcNow();
            LastResult = result;
            LastError = null;
            _log.LogInformation(
                "Household run: published={Published}, segments applied={Segments}, operations applied={Applied}, problems={Problems}",
                result.OperationsPublished, result.SegmentsApplied, result.OperationsApplied, result.Problems.Count);
            foreach (var problem in result.Problems) _log.LogWarning("Household: {Problem}", problem);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The storage may be offline or need a new sign-in; the next run
            // retries. The message names no secret.
            LastRunAt = _clock.GetUtcNow();
            LastError = ex.Message;
            _log.LogWarning(ex, "Household run failed.");
            return null;
        }
        finally
        {
            _run.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public override void Dispose()
    {
        _run.Dispose();
        _wake.Dispose();
        base.Dispose();
    }
}
