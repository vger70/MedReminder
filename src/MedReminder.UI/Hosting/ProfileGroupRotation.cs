using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Profiles;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MedReminder.UI.Hosting;

// IProfileGroupRotation for the desktop (household step H5b; docs/analysis/
// ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §9 points 2 and 5). The open profile
// rotates in the application's services while its sync service is idle;
// another profile rotates in its own services (ProfileServices). Both run
// the profile's sync first (RotateSyncKey needs the latest state), then
// RotateSyncKey with a random passphrase that is never shown or stored:
// the devices get the new key through their household grants.
internal sealed class ProfileGroupRotation : IProfileGroupRotation
{
    private readonly IServiceProvider _services;
    private readonly ICurrentProfile _current;
    private readonly IProfileRegistry _registry;
    private readonly SyncHostedService _sync;

    public ProfileGroupRotation(IServiceProvider services, ICurrentProfile current, IProfileRegistry registry,
        SyncHostedService sync)
    {
        _services = services;
        _current = current;
        _registry = registry;
        _sync = sync;
    }

    public async Task<ProfileGroupKey> RotateAsync(string profileId, CancellationToken cancellationToken)
    {
        if (profileId == _current.Id)
        {
            await _sync.RunNowAsync(cancellationToken);
            var key = await _sync.WhileIdleAsync(() => RotateInAsync(_services, cancellationToken), cancellationToken);
            await _sync.RunNowAsync(cancellationToken);
            return key;
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
        return await RotateInAsync(provider, cancellationToken);
    }

    private static async Task<ProfileGroupKey> RotateInAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var passphrase = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).ToCharArray();
        try
        {
            var settings = await sp.GetRequiredService<RotateSyncKey>()
                .ExecuteAsync(sp.GetRequiredService<ISyncTransport>(), passphrase, ct);
            var key = sp.GetRequiredService<ISyncKeyStore>().Load(settings.GroupId, settings.KeyVersion)
                ?? throw new InvalidOperationException("The new group key was not stored.");
            return new ProfileGroupKey(settings.GroupId, settings.KeyVersion, key);
        }
        finally
        {
            Array.Clear(passphrase);
        }
    }
}
