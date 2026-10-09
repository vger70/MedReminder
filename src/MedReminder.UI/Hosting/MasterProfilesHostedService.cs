using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Sync;
using MedReminder.Infrastructure;
using MedReminder.Infrastructure.Cloud.GoogleDrive;
using MedReminder.Infrastructure.Cloud.OneDrive;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Profiles;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Hosting;

// Household step H4c (docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md
// §7.6, C4): the active master evaluates every profile it holds, not only
// the open one. Every Master:ProfilesIntervalMinutes (default 15), for each
// other profile of this device it builds that profile's services (the same
// registrations as the application, with that profile's database, sync
// state and notification recipients), then:
//
//   1. applies the schema patches (DatabaseInitializer);
//   2. syncs the profile's group, so the stock is current;
//   3. runs the consumption catch-up and the low-stock and dose checks,
//      email only: an on-screen reminder for a profile nobody opened
//      would reach the wrong person;
//   4. syncs again, so the other devices learn the emails sent
//      (EmailNotificationSent).
//
// Idle unless this device is the active master of an election (with no
// master elected, every device handles its open profile as before). The
// profiles run one after the other; an error in one is logged and the
// others go on. All writes happen in this process, which owns every
// profile database (single-instance mutex, CLAUDE.md §7); the static write
// gate serializes them with the open profile's.
internal sealed class MasterProfilesHostedService : BackgroundService
{
    private const int DefaultIntervalMinutes = 15;
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(90);

    private readonly IServiceProvider _services;
    private readonly ICurrentProfile _current;
    private readonly IProfileRegistry _registry;
    private readonly TimeProvider _clock;
    private readonly ILogger<MasterProfilesHostedService> _log;
    private readonly TimeSpan _interval;

    public MasterProfilesHostedService(IServiceProvider services, ICurrentProfile current, IProfileRegistry registry,
        TimeProvider clock, IConfiguration configuration, ILogger<MasterProfilesHostedService> log)
    {
        _services = services;
        _current = current;
        _registry = registry;
        _clock = clock;
        _log = log;
        var configured = int.TryParse(configuration["Master:ProfilesIntervalMinutes"], out var v) ? v : DefaultIntervalMinutes;
        _interval = TimeSpan.FromMinutes(Math.Max(1, configured));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, _clock, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(_interval, _clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Clean shutdown.
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        MasterView view;
        await using (var scope = _services.CreateAsyncScope())
        {
            view = await scope.ServiceProvider.GetRequiredService<HouseholdMasterRole>().DescribeAsync(cancellationToken);
        }
        if (view.Master.Election is null || !view.SendsEmail) return;

        foreach (var profile in _registry.ListProfiles().Where(p => p.Id != _current.Id))
        {
            try
            {
                await RunProfileAsync(profile, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Master: the checks of profile {ProfileId} failed.", profile.Id);
            }
        }
    }

    private async Task RunProfileAsync(Profile profile, CancellationToken ct)
    {
        var current = new CurrentProfile(profile, AppDataPaths.GetProfilesRootDirectory());
        if (!File.Exists(current.DatabasePath)) return;

        await using var provider = ProfileServices.Build(_services, current);
        // Step H5b: a group rotated after a device removal; the new key comes
        // from the household grant.
        await ProfileServices.AdoptRotatedKeyAsync(provider, current, _log, ct);
        await Scoped(provider, sp => sp.GetRequiredService<DatabaseInitializer>().InitializeAsync(ct));
        var synced = provider.GetRequiredService<ISyncSettingsStore>().Load() is not null;
        if (synced) await SyncAsync(provider, profile, ct);

        var created = await Scoped(provider, sp => sp.GetRequiredService<ConsumptionCatchUp>().RunAsync(ct));
        var low = await Scoped(provider, sp => sp.GetRequiredService<MedicationMonitor>().RunAsync(ct));
        var doses = await Scoped(provider, sp => sp.GetRequiredService<DoseReminderService>().RunAsync(ct));
        if (synced) await SyncAsync(provider, profile, ct);

        _log.LogInformation(
            "Master: profile {ProfileId} checked; consumption rows={Created}, low-stock notifications={Low}, dose reminders={Doses}.",
            profile.Id, created, low.NotificationsSent, doses.FiredCount);
    }

    private async Task SyncAsync(ServiceProvider provider, Profile profile, CancellationToken ct)
    {
        var result = await Scoped(provider, sp => sp.GetRequiredService<SyncEngine>().RunAsync(ct));
        if (result.Problems.Count > 0 || result.RebuildRequired || result.NewKeyRequired || result.NewerGeneration is not null)
        {
            // The open profile's sync window repairs these; here the checks
            // go on with what the database holds.
            _log.LogWarning("Master: the sync of profile {ProfileId} needs attention ({Problems} problems).",
                profile.Id, result.Problems.Count);
        }
    }

    private static async Task<T> Scoped<T>(IServiceProvider provider, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private static Task Scoped(IServiceProvider provider, Func<IServiceProvider, Task> action)
        => Scoped<bool>(provider, async sp => { await action(sp); return true; });
}
