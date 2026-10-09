using FluentAssertions;
using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Email;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Composition;

// Android plan M0 (B0-03): a host that references only Application and
// Infrastructure.Portable composes the profile stores and the
// notification planner, with its own adapters for the ports the
// Windows layer fills on the desktop.
public sealed class PortableCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-compose-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Portable_host_plans_notifications_for_a_new_profile()
    {
        var profilesRoot = Path.Combine(_root, "profiles");
        var registry = new ProfileRegistry(Path.Combine(_root, "profiles.json"), profilesRoot, TimeProvider.System);
        var current = new CurrentProfile(registry.Create("Anna", ProfileRole.Admin), profilesRoot);

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<IAppDataLocation>(new Location(_root));
        services.AddSingleton<IProfileRegistry>(registry);
        services.AddSingleton<ICurrentProfile>(current);
        services.Configure<SmtpSettings>(_ => { });
        services.Configure<BackupSettings>(_ => { });
        services.Configure<UserSettings>(_ => { });
        services.AddMedReminderApplication();
        services.AddMedReminderPortableInfrastructure(current.DatabasePath);
        services.AddMedReminderPortableProfileSettings(profilesRoot);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);

        scope.ServiceProvider.GetRequiredService<IProfileSettingsStore>().Read()[ProfileSetting.DisplayName]
            .Should().Be("Anna");
        scope.ServiceProvider.GetRequiredService<ISyncProfileStatus>().IsSyncEnabled(current.Id).Should().BeFalse();
        scope.ServiceProvider.GetRequiredService<IInstallationSettingsStore>().Should().NotBeNull();
        var plan = await scope.ServiceProvider.GetRequiredService<NotificationPlanLoader>()
            .PlanAsync(options: null, CancellationToken.None);
        plan.Should().BeEmpty();
    }

    private sealed class Location(string directory) : IAppDataLocation
    {
        public string DataDirectory => directory;
    }
}
