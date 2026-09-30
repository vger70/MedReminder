using System.Security.Cryptography;
using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Application.Sync;
using MedReminder.Infrastructure;
using MedReminder.Infrastructure.Cloud.GoogleDrive;
using MedReminder.Infrastructure.Cloud.OneDrive;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Hosting;

// The services of one profile of this installation, whether it is the
// open one or not (household steps H4c and H5b): the registrations of
// Program.BuildHost with that profile's database, sync state and
// notification recipients, without hosted services or forms, and with no
// on-screen notification. The cloud accounts, the profile registry and the
// household store are this process's single instances, taken from the
// application. The caller disposes the provider.
internal static class ProfileServices
{
    public static ServiceProvider Build(IServiceProvider application, ICurrentProfile profile)
    {
        var builder = new ConfigurationBuilder();
        Program.AddProfileConfiguration(builder, profile, reloadOnChange: false);
        var configuration = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(application.GetRequiredService<ILoggerFactory>());
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(application.GetRequiredService<TimeProvider>());
        services.AddSingleton(application.GetRequiredService<SyncEngineOptions>());
        services.AddSingleton(application.GetRequiredService<IProfileRegistry>());
        services.AddSingleton(application.GetRequiredService<IHouseholdStore>());
        services.AddSingleton(application.GetRequiredService<ICloudAccountService>());
        services.AddSingleton(application.GetRequiredService<IOneDriveAccessTokens>());
        services.AddSingleton(application.GetRequiredService<IGoogleDriveAccessTokens>());

        services.AddMedReminderApplication();
        services.AddMedReminderInfrastructure(configuration, profile);
        services.RemoveAll<IWindowsNotificationService>();
        services.AddSingleton<IWindowsNotificationService, SilentWindowsNotifications>();
        return services.BuildServiceProvider();
    }

    // Step H5b (§9 point 3): when the household grants this device a newer
    // key for the profile's group (rotated after a device removal), the
    // profile is rebuilt from the new generation with it and this device's
    // own operations are carried over (ISyncSetupService.RekeyAsync), with
    // no passphrase: the rotation's passphrase is random and never shown.
    // Runs only while the profile's database is not in use: at start for
    // the open profile, or on a profile that is not open. True when done.
    public static async Task<bool> AdoptRotatedKeyAsync(IServiceProvider services, ICurrentProfile profile,
        ILogger log, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        if (sp.GetRequiredService<ISyncSettingsStore>().Load() is not { } settings) return false;
        var key = await sp.GetRequiredService<HouseholdKeyring>()
            .NewerGrantAsync(profile.Id, settings.GroupId, settings.KeyVersion, cancellationToken);
        if (key is null) return false;
        try
        {
            var (carried, dropped) = await sp.GetRequiredService<ISyncSetupService>()
                .RekeyAsync(new SyncKeySource.Known(key.KeyVersion, key.Key), cancellationToken);
            log.LogInformation(
                "Profile {ProfileId} took key version {KeyVersion} from the household: {Carried} own operations carried over, {Dropped} dropped.",
                profile.Id, key.KeyVersion, carried, dropped);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key.Key);
        }
    }

    private sealed class SilentWindowsNotifications : IWindowsNotificationService
    {
        public Task ShowAsync(string title, string body, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
