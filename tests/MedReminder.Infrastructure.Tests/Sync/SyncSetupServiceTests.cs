using System.Runtime.Versioning;
using FluentAssertions;
using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using MedReminder.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Sync;

// B.1 Phase 3d on Windows: enabling sync on one profile and joining it
// from another replaces the second profile's database like an import
// (safety copy kept, settings and key saved). Real profile folders under
// %LOCALAPPDATA%\MedReminder\profiles\, removed on Dispose.
[SupportedOSPlatform("windows")]
public sealed class SyncSetupServiceTests : IDisposable
{
    private const string Passphrase = "sync passphrase for tests";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mr-sync-setup-" + Guid.NewGuid().ToString("N"));
    private readonly List<(ServiceProvider Provider, string ProfileId)> _profiles = new();

    public void Dispose()
    {
        foreach (var (provider, _) in _profiles) provider.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var (_, id) in _profiles)
        {
            try { Directory.Delete(AppDataPaths.GetProfileDataDirectory(id), recursive: true); } catch { /* best effort */ }
        }
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Joining_replaces_the_profile_database_and_saves_the_group()
    {
        var a = await CreateProfileAsync("Enalapril");
        await Run(a, sp => sp.GetRequiredService<ISyncSetupService>()
            .CreateAsync(_folder, Passphrase.ToCharArray(), "PC A", CancellationToken.None));

        var b = await CreateProfileAsync("Local only");
        var groups = await Run(b, sp => sp.GetRequiredService<ISyncSetupService>().ListGroupsAsync(_folder, CancellationToken.None));
        groups.Should().ContainSingle();
        await Run(b, sp => sp.GetRequiredService<ISyncSetupService>()
            .JoinAsync(_folder, groups[0], Passphrase.ToCharArray(), "PC B", CancellationToken.None));

        var medicines = await Run(b, sp => sp.GetRequiredService<IMedicineRepository>().ListAllAsync(CancellationToken.None));
        medicines.Select(m => m.Name).Should().Equal("Enalapril");
        var settings = b.Provider.GetRequiredService<ISyncSettingsStore>().Load();
        settings!.GroupId.Should().Be(groups[0]);
        settings.DeviceName.Should().Be("PC B");
        b.Provider.GetRequiredService<ISyncKeyStore>().Load(settings.GroupId, 1).Should().NotBeNull();
        Directory.EnumerateFiles(AppDataPaths.GetProfileDataDirectory(b.ProfileId), "medreminder.db.bak-*")
            .Should().ContainSingle();
    }

    private async Task<(ServiceProvider Provider, string ProfileId)> CreateProfileAsync(string medicine)
    {
        var id = Guid.NewGuid().ToString("N");
        var dir = AppDataPaths.GetProfileDataDirectory(id);
        Directory.CreateDirectory(dir);
        var profile = new FakeProfile(id, dir);

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentProfile>(profile);
        services.AddSingleton<ISyncKeyStore, MemoryKeyStore>();
        services.AddMedReminderPortableInfrastructure(profile.DatabasePath);
        services.AddMedReminderApplication();
        services.AddScoped<ISyncSetupService, SyncSetupService>();
        var entry = (services.BuildServiceProvider(), id);
        _profiles.Add(entry);

        await Run(entry, sp => sp.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None));
        await Run(entry, sp => sp.GetRequiredService<AddMedicine>().ExecuteAsync(new AddMedicineCommand(
            medicine, "tablet", 1m, 1, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows), CancellationToken.None));
        return entry;
    }

    private static async Task<T> Run<T>((ServiceProvider Provider, string ProfileId) profile, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = profile.Provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private static Task Run((ServiceProvider Provider, string ProfileId) profile, Func<IServiceProvider, Task> action)
        => Run(profile, async sp => { await action(sp); return true; });

    private sealed class FakeProfile(string id, string directory) : ICurrentProfile
    {
        public string Id => id;
        public string DisplayName => "Test";
        public ProfileRole Role => ProfileRole.Admin;
        public bool IsAdmin => true;
        public string DataDirectory => directory;
        public string DatabasePath => Path.Combine(directory, AppDataPaths.DatabaseFileName);
        public string NotificationSettingsPath => Path.Combine(directory, "notifications.settings.json");
    }

    private sealed class MemoryKeyStore : ISyncKeyStore
    {
        private readonly Dictionary<(Guid, int), byte[]> _keys = new();
        public byte[]? Load(Guid groupId, int keyVersion) => _keys.TryGetValue((groupId, keyVersion), out var k) ? [.. k] : null;
        public void Save(Guid groupId, int keyVersion, byte[] key) => _keys[(groupId, keyVersion)] = [.. key];
        public void Clear() => _keys.Clear();
    }
}
