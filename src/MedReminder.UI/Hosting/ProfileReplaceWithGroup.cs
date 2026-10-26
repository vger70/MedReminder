using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Infrastructure.Household;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Profiles;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MedReminder.UI.Hosting;

// IProfileReplaceWithGroup for the desktop (household step H3d; docs/analysis/
// ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §6.4). The open profile is replaced in
// the application's services while its sync service is suspended (the swap
// moves the database file, like a rebuild); another profile is replaced in
// its own services (ProfileServices), whose database is initialised and
// synced first so the swap sees the latest state. The restart that follows
// belongs to the caller.
internal sealed class ProfileReplaceWithGroup : IProfileReplaceWithGroup
{
    private readonly IServiceProvider _services;
    private readonly ICurrentProfile _current;
    private readonly IProfileRegistry _registry;
    private readonly SyncHostedService _sync;

    public ProfileReplaceWithGroup(IServiceProvider services, ICurrentProfile current, IProfileRegistry registry,
        SyncHostedService sync)
    {
        _services = services;
        _current = current;
        _registry = registry;
        _sync = sync;
    }

    public async Task ReplaceAsync(string profileId, string deviceName, CancellationToken cancellationToken)
    {
        if (profileId == _current.Id)
        {
            // The swap moves the database file: sync must stop touching it,
            // and the host restarts afterwards (like a rebuild).
            await _sync.SuspendAsync(cancellationToken);
            await ReplaceInAsync(_services, profileId, deviceName, cancellationToken);
            return;
        }

        var profile = _registry.GetById(profileId)
            ?? throw new InvalidOperationException("The profile is not on this device.");
        await using var provider = ProfileServices.Build(_services, new CurrentProfile(profile, AppDataPaths.GetProfilesRootDirectory()));
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SyncEngine>().RunAsync(cancellationToken);
        }
        await ReplaceInAsync(provider, profileId, deviceName, cancellationToken);
    }

    private static async Task ReplaceInAsync(IServiceProvider services, string profileId, string deviceName,
        CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HouseholdProfileReplace>().ExecuteAsync(deviceName, ct);
    }
}
