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
// a later tick retries, and a missing folder must be detected before
// the (Argon2id) export runs. Every registered profile is exported,
// and a failure on one profile does not stop the others.
public sealed class AutomaticBackupHostedServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBackupStateStore _state = new();
    private readonly RecordingArchiveStorage _storage = new();
    private readonly CountingExportService _export = new();
    private readonly string _cloudFolder;

    public AutomaticBackupHostedServiceTests()
    {
        _cloudFolder = Path.Combine(
            Path.GetTempPath(), "mr-host-cloud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cloudFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_cloudFolder, recursive: true); } catch { /* best effort */ }
    }

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
    public async Task Cloud_only_tick_with_a_missing_folder_skips_before_exporting()
    {
        var host = CreateHost(
            hasPassphrase: true, cloudFolder: Path.Combine(_cloudFolder, "missing"));

        await host.TryRunAsync(CancellationToken.None);

        _export.Calls.Should().Be(0, "a missing folder must not cost an Argon2id export");
        _storage.Uploads.Should().BeEmpty();
        var state = _state.Load();
        state.LastSuccessfulBackupAt.Should().BeNull();
        state.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Cloud_only_tick_whose_folder_disappears_during_the_export_is_skipped_without_error()
    {
        _storage.UploadFailure = new DirectoryNotFoundException("missing");
        var host = CreateHost(hasPassphrase: true);

        await host.TryRunAsync(CancellationToken.None);

        _export.Calls.Should().Be(1);
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
        state.LastError.Should().Be($"cloud {ProfileId}: disk full");
    }

    [Fact]
    public async Task Cloud_tick_exports_every_registered_profile()
    {
        var host = CreateHost(hasPassphrase: true, profileIds: new[] { ProfileId, OtherProfileId });

        await host.TryRunAsync(CancellationToken.None);

        _export.ProfileIds.Should().Equal(ProfileId, OtherProfileId);
        _storage.Uploads.Should().HaveCount(2);
        _storage.Uploads.Should().Contain(n => n.StartsWith($"medreminder-{ProfileId}-"));
        _storage.Uploads.Should().Contain(n => n.StartsWith($"medreminder-{OtherProfileId}-"));
        _state.Load().LastSuccessfulBackupAt.Should().Be(Now);
    }

    [Fact]
    public async Task Cloud_failure_on_one_profile_does_not_stop_the_others()
    {
        _export.FailingProfileId = ProfileId;
        var host = CreateHost(hasPassphrase: true, profileIds: new[] { ProfileId, OtherProfileId });

        await host.TryRunAsync(CancellationToken.None);

        _storage.Uploads.Should().ContainSingle()
            .Which.Should().StartWith($"medreminder-{OtherProfileId}-");
        var state = _state.Load();
        state.LastSuccessfulBackupAt.Should().Be(Now);
        state.LastError.Should().Be($"cloud {ProfileId}: export failed");
    }

    private AutomaticBackupHostedService CreateHost(
        bool hasPassphrase, string? cloudFolder = null, string[]? profileIds = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBackupService, LocalTargetUnusedBackupService>();
        services.AddSingleton<IProfileRegistry>(new FixedProfileRegistry(profileIds ?? new[] { ProfileId }));
        services.AddSingleton<IArchiveStorage>(_storage);
        services.AddSingleton<ICloudBackupPassphraseStore>(new FakePassphraseStore(hasPassphrase));
        services.AddSingleton<IExportService>(_export);
        services.AddSingleton<ICurrentProfile, FakeCurrentProfile>();

        var settings = new BackupSettings
        {
            Enabled = false,
            CloudFolderEnabled = true,
            CloudFolderDirectory = cloudFolder ?? _cloudFolder,
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
    private const string OtherProfileId = "fedcba9876543210fedcba9876543210";

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

    private sealed class CountingExportService : IExportService
    {
        public int Calls { get; private set; }
        public List<string?> ProfileIds { get; } = new();
        public string? FailingProfileId { get; set; }

        public async Task<string> ExportAsync(
            ExportOptions options, char[] passphrase, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            ProfileIds.Add(options.ProfileId);
            if (options.ProfileId == FailingProfileId)
            {
                throw new InvalidOperationException("export failed");
            }
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

    private sealed class FixedProfileRegistry(string[] ids) : IProfileRegistry
    {
        public IReadOnlyList<Profile> ListProfiles() =>
            ids.Select(id => new Profile(id, "Test " + id[..4], ProfileRole.Admin, Now, Now, HasPin: false))
               .ToList();

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
