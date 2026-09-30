using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Sync;
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

    public SyncDevice(string name, string databasePath, DateTimeOffset now, SyncSettings? settings,
        byte[]? key = null, int checkpointEvery = 5000, Func<ISyncTransport, ISyncTransport>? transport = null,
        Func<TimeProvider, ISyncTransport>? remote = null)
    {
        Name = name;
        DatabasePath = databasePath;
        Clock = new SettableClock(now);
        Settings = new FixedSyncSettingsStore(settings);
        Keys = new MemoryKeyStore();
        ProfileSettings = new MemoryProfileSettingsStore(name);
        if (settings is not null && key is not null) Keys.Save(settings.GroupId, settings.KeyVersion, key);

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ISyncSettingsStore>(Settings);
        services.AddSingleton<ISyncKeyStore>(Keys);
        services.AddSingleton<IProfileSettingsStore>(ProfileSettings);
        services.AddSingleton<IEmailNotificationService>(Emails);
        services.AddSingleton<IWindowsNotificationService>(Toasts);
        // Household step H4a: the master role reads the installation's
        // household, a household of one here (every device sends).
        services.AddSingleton<IAppDataLocation>(new DataLocation(
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, name + "-appdata")));
        // A join waits for its record to be listed (JoinSyncGroup); JoinDelay
        // stands for the seconds between two listings.
        services.AddSingleton(new SyncEngineOptions
        {
            DeviceName = name, CheckpointEvery = checkpointEvery, Delay = (t, ct) => JoinDelay(t, ct),
        });
        if (transport is not null)
        {
            services.AddScoped(sp => transport(new LocalFolderSyncTransport(
                sp.GetRequiredService<ISyncSettingsStore>().Load()!.Folder!)));
        }
        if (remote is not null)
        {
            // A provider transport (Phase 4a), one per device like the app's.
            services.AddSingleton(remote(Clock));
        }
        services.AddMedReminderPortableInfrastructure(databasePath);
        services.AddMedReminderApplication();
        // The monitor's texts need no dictionary here (after the portable
        // registration, which it replaces).
        services.AddSingleton<ILocalizationService, KeyLocalization>();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public string Name { get; }

    public string DatabasePath { get; }

    public SettableClock Clock { get; }

    public FixedSyncSettingsStore Settings { get; }

    public MemoryKeyStore Keys { get; }

    public MemoryProfileSettingsStore ProfileSettings { get; }

    // What MedicationMonitor sent from this device.
    public RecordingEmail Emails { get; } = new();

    public RecordingToasts Toasts { get; } = new();

    // What happens while a join run on this device waits between two
    // listings. Immediate by default.
    public Func<TimeSpan, CancellationToken, Task> JoinDelay { get; set; } = (_, _) => Task.CompletedTask;

    public Task<SyncRunResult> SyncAsync()
        => RunAsync(sp => sp.GetRequiredService<SyncEngine>().RunAsync(CancellationToken.None));

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

    private sealed class DataLocation(string directory) : IAppDataLocation
    {
        public string DataDirectory => directory;
    }

    private sealed class KeyLocalization : ILocalizationService
    {
        public string CurrentLanguage => "en";

        public System.Globalization.CultureInfo CurrentCulture => System.Globalization.CultureInfo.InvariantCulture;

        public string Get(string key, params object?[] args) => key;

        public string GetIn(string languageCode, string key, params object?[] args) => key;
    }

    internal sealed class RecordingEmail : IEmailNotificationService
    {
        public List<MedReminder.Application.Notifications.EmailMessage> Sent { get; } = [];

        public Task SendAsync(MedReminder.Application.Notifications.EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    internal sealed class RecordingToasts : IWindowsNotificationService
    {
        public List<string> Shown { get; } = [];

        public Task ShowAsync(string title, string body, CancellationToken cancellationToken)
        {
            Shown.Add(title);
            return Task.CompletedTask;
        }
    }

    internal sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Set(DateTimeOffset now) => _now = now.ToUniversalTime();

        public void Advance(TimeSpan delta) => _now += delta;
    }

    internal sealed class MemoryKeyStore : ISyncKeyStore
    {
        private readonly Dictionary<(Guid, int), byte[]> _keys = new();

        public byte[]? Load(Guid groupId, int keyVersion)
            => _keys.TryGetValue((groupId, keyVersion), out var key) ? [.. key] : null;

        public void Save(Guid groupId, int keyVersion, byte[] key) => _keys[(groupId, keyVersion)] = [.. key];

        public void Clear() => _keys.Clear();
    }

    // The profile registry and notifications.settings.json of the device.
    internal sealed class MemoryProfileSettingsStore(string displayName) : IProfileSettingsStore
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal)
        {
            [ProfileSetting.DisplayName] = displayName,
            [ProfileSetting.ToAddress] = string.Empty,
            [ProfileSetting.CaregiverAddress] = string.Empty,
            [ProfileSetting.DoctorAddress] = string.Empty,
        };

        public IReadOnlyDictionary<string, string?> Read() => new Dictionary<string, string?>(_values, StringComparer.Ordinal);

        public void Write(IReadOnlyDictionary<string, string?> changes)
        {
            foreach (var (key, value) in changes) _values[key] = value;
        }
    }

    internal sealed class FixedSyncSettingsStore(SyncSettings? settings) : ISyncSettingsStore
    {
        private SyncSettings? _settings = settings;

        public SyncSettings? Load() => _settings;

        public void Save(SyncSettings? value) => _settings = value;
    }
}
