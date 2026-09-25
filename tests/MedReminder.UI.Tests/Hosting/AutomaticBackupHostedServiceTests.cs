using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.UI.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedReminder.UI.Tests.Hosting;

// Cloud-only configuration (local raw-DB target off): a written .mrz
// must mark the day as backed up, otherwise every 15-minute tick after
// the preferred time would re-export. Skips must leave the day open so
// a later tick retries.
public sealed class AutomaticBackupHostedServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBackupStateStore _state = new();
    private readonly RecordingArchiveStorage _storage = new();

    [Fact]
    public async Task Cloud_only_tick_that_writes_an_archive_marks_the_day_as_backed_up()
    {
        var host = CreateHost(hasPassphrase: true);

        await host.TryRunAsync(CancellationToken.None);
        await host.TryRunAsync(CancellationToken.None);

        _storage.Uploads.Should().ContainSingle("the second tick of the same day must not re-export");
        var state = _state.Load();
        state.LastSuccessfulBackupAt.Should().Be(Now);
        state.LastError.Should().BeNull();
        state.LastBackupFile.Should().Be(RecordingArchiveStorage.IdFor(_storage.Uploads[0]));
    }

    [Fact]
    public async Task Cloud_only_tick_without_a_passphrase_leaves_the_day_open()
    {
        var host = CreateHost(hasPassphrase: false);

        await host.TryRunAsync(CancellationToken.None);

        _storage.Uploads.Should().BeEmpty();
        var state = _state.Load();
        state.LastSuccessfulBackupAt.Should().BeNull();
        state.LastAttemptAt.Should().Be(Now);
        state.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Cloud_only_tick_with_a_missing_folder_is_skipped_without_error()
    {
        _storage.UploadFailure = new DirectoryNotFoundException("missing");
        var host = CreateHost(hasPassphrase: true);

        await host.TryRunAsync(CancellationToken.None);

        var state = _state.Load();
        state.LastSuccessfulBackupAt.Should().BeNull();
        state.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Cloud_only_tick_with_a_failed_upload_records_the_error_and_leaves_the_day_open()
    {
        _storage.UploadFailure = new IOException("disk full");
        var host = CreateHost(hasPassphrase: true);

        await host.TryRunAsync(CancellationToken.None);

        var state = _state.Load();
        state.LastSuccessfulBackupAt.Should().BeNull();
        state.LastError.Should().Be("cloud: disk full");
    }

    private AutomaticBackupHostedService CreateHost(bool hasPassphrase)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBackupService, LocalTargetUnusedBackupService>();
        services.AddSingleton<IProfileRegistry, SingleProfileRegistry>();
        services.AddSingleton<IArchiveStorage>(_storage);
        services.AddSingleton<ICloudBackupPassphraseStore>(new FakePassphraseStore(hasPassphrase));
        services.AddSingleton<IExportService, FakeExportService>();
        services.AddSingleton<ICurrentProfile, FakeCurrentProfile>();

        var settings = new BackupSettings
        {
            Enabled = false,
            CloudFolderEnabled = true,
            CloudFolderDirectory = "cloud",
            CloudFolderRetention = 30,
            PreferredTime = "03:00",
        };

        return new AutomaticBackupHostedService(
            services.BuildServiceProvider(),
            new StaticOptionsMonitor(settings),
            _state,
            new FixedClock(Now),
            NullLogger<AutomaticBackupHostedService>.Instance);
    }

    private const string ProfileId = "0123456789abcdef0123456789abcdef";

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class StaticOptionsMonitor(BackupSettings value) : IOptionsMonitor<BackupSettings>
    {
        public BackupSettings CurrentValue => value;
        public BackupSettings Get(string? name) => value;
        public IDisposable? OnChange(Action<BackupSettings, string?> listener) => null;
    }

    private sealed class InMemoryBackupStateStore : IBackupStateStore
    {
        private BackupState _state = BackupState.Empty;
        public BackupState Load() => _state;
        public void Save(BackupState state) => _state = state;
    }

    private sealed class RecordingArchiveStorage : IArchiveStorage
    {
        public List<string> Uploads { get; } = new();
        public Exception? UploadFailure { get; set; }

        public static string IdFor(string name) => "archive:" + name;

        public async Task<string> UploadAsync(Stream archive, string suggestedName, CancellationToken ct)
        {
            await using (archive)
            {
                if (UploadFailure is not null) throw UploadFailure;
                await archive.CopyToAsync(Stream.Null, ct);
                Uploads.Add(suggestedName);
                return IdFor(suggestedName);
            }
        }

        public Task<Stream> DownloadAsync(string id, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<ArchiveInfo>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ArchiveInfo>>(Array.Empty<ArchiveInfo>());

        public Task DeleteAsync(string id, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakePassphraseStore(bool hasPassphrase) : ICloudBackupPassphraseStore
    {
        public bool HasPassphrase => hasPassphrase;
        public char[]? GetPassphrase() => hasPassphrase ? "test-passphrase-123".ToCharArray() : null;
        public void SetPassphrase(char[] passphrase) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }

    private sealed class FakeExportService : IExportService
    {
        public async Task<string> ExportAsync(
            ExportOptions options, char[] passphrase, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            await File.WriteAllBytesAsync(options.DestinationPath, new byte[] { 1, 2, 3 }, cancellationToken);
            return options.DestinationPath;
        }
    }

    private sealed class FakeCurrentProfile : ICurrentProfile
    {
        public string Id => ProfileId;
        public string DisplayName => "Test";
        public ProfileRole Role => ProfileRole.Admin;
        public bool IsAdmin => true;
        public string DataDirectory => string.Empty;
        public string DatabasePath => string.Empty;
        public string NotificationSettingsPath => string.Empty;
    }

    private sealed class SingleProfileRegistry : IProfileRegistry
    {
        public IReadOnlyList<Profile> ListProfiles() =>
            new[] { new Profile(ProfileId, "Test", ProfileRole.Admin, Now, Now, HasPin: false) };

        public Profile? GetById(string id) => ListProfiles().SingleOrDefault(p => p.Id == id);
        public string? ActiveProfileIdHint => ProfileId;
        public Profile Create(string displayName, ProfileRole role) => throw new NotSupportedException();
        public void Rename(string id, string newDisplayName) => throw new NotSupportedException();
        public void Delete(string id, bool deleteData) => throw new NotSupportedException();
        public void SetActiveProfileHint(string id) => throw new NotSupportedException();
        public void SetPin(string id, string? pin) => throw new NotSupportedException();
        public bool VerifyPin(string id, string pin) => throw new NotSupportedException();
        public bool HasPin(string id) => false;
    }

    // The local raw-DB target is disabled in every test here.
    private sealed class LocalTargetUnusedBackupService : IBackupService
    {
        public string DatabasePath => string.Empty;

        public Task<string> ExportProfileAsync(
            string profileId, string destinationDirectory, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The local target must not run.");

        public Task<int> PruneOldBackupsAsync(
            string directory, int retentionDays, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The local target must not run.");

        public Task ImportProfileAsync(
            string profileId, string sourceFilePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> PruneCloudFolderAsync(
            IArchiveStorage storage, int retentionDays, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
