using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedReminder.Infrastructure.Tests.Sync;

// One simulated installation for the convergence tests: its own SQLite
// file, clock and sync settings, wired like the desktop host (the real
// repositories, use cases and DI lifetimes, one scope per action).
internal sealed class SyncDevice : IDisposable
{
    private readonly ServiceProvider _provider;

    public SyncDevice(string name, string databasePath, DateTimeOffset now, SyncSettings? settings)
    {
        Name = name;
        DatabasePath = databasePath;
        Clock = new SettableClock(now);
        Settings = new FixedSyncSettingsStore(settings);

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ISyncSettingsStore>(Settings);
        services.AddMedReminderPortableInfrastructure(databasePath);
        services.AddMedReminderApplication();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public string Name { get; }

    public string DatabasePath { get; }

    public SettableClock Clock { get; }

    public FixedSyncSettingsStore Settings { get; }

    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task RunAsync(Func<IServiceProvider, Task> action)
        => RunAsync<bool>(async sp => { await action(sp); return true; });

    public Task InitializeAsync()
        => RunAsync(sp => sp.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None));

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
    }

    internal sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Set(DateTimeOffset now) => _now = now.ToUniversalTime();

        public void Advance(TimeSpan delta) => _now += delta;
    }

    internal sealed class FixedSyncSettingsStore(SyncSettings? settings) : ISyncSettingsStore
    {
        private SyncSettings? _settings = settings;

        public SyncSettings? Load() => _settings;

        public void Save(SyncSettings? value) => _settings = value;
    }
}
