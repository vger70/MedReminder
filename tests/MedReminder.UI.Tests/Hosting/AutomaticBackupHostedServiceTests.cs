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
// and a failure on one profile does not stop the others. A failed tick,
// a disconnected account included, notifies the user at most once a day.
// The local and cloud targets keep separate days, and the tick's result is
// applied to the latest state, not to the copy read before the exports.
public sealed class AutomaticBackupHostedServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBackupStateStore _state = new();
    private readonly RecordingArchiveStorage _storage = new();
    private readonly CountingExportService _export = new();
    private readonly RecordingNotifications _notifications = new();
    private readonly MutableClock _clock = new(Now);
    private readonly LocalBackupService _local = new();
    private readonly FakeAccounts _accounts = new();
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

    [Fact]
    public async Task OneDrive_tick_with_a_signed_in_account_uploads_without_a_folder()
    {
        var host = CreateHost(hasPassphrase: true, cloudFolder: string.Empty, oneDriveAccount: "account-1",
            signedIn: true);

        await host.TryRunAsync(CancellationToken.None);

        _storage.Uploads.Should().ContainSingle();
        _state.Load().LastSuccessfulBackupAt.Should().Be(Now);
    }

    [Fact]
    public async Task OneDrive_tick_with_a_signed_out_account_records_it_before_the_export()
    {
        var host = CreateHost(hasPassphrase: true, cloudFolder: string.Empty, oneDriveAccount: "account-1",
            signedIn: false);

        await host.TryRunAsync(CancellationToken.None);

        _export.Calls.Should().Be(0);
        _storage.Uploads.Should().BeEmpty();
        var state = _state.Load();
        state.LastSuccessfulBackupAt.Should().BeNull();
        state.SignInRequired.Should().Be(CloudProvider.OneDrive);
        state.LastError.Should().BeNull();
    }

    [Fact]
    public async Task OneDrive_tick_with_an_expired_session_records_it_before_the_export()
    {
        // The account is still in the token cache, but the refresh token
        // was revoked (password change): FindAsync alone would not notice.
        var host = CreateHost(hasPassphrase: true, cloudFolder: string.Empty, oneDriveAccount: "account-1",
            signedIn: true, hasSession: false);

        await host.TryRunAsync(CancellationToken.None);

        _export.Calls.Should().Be(0, "an ended session must not cost an Argon2id export");
        var state = _state.Load();
        state.LastSuccessfulBackupAt.Should().BeNull();
        state.SignInRequired.Should().Be(CloudProvider.OneDrive);
        state.LastError.Should().BeNull();
    }

    [Fact]
    public async Task OneDrive_session_ending_during_the_export_is_recorded_and_stops_the_run()
    {
        _storage.UploadFailure = new CloudSignInRequiredException(CloudProvider.OneDrive);
        var host = CreateHost(hasPassphrase: true, cloudFolder: string.Empty, oneDriveAccount: "account-1",
            signedIn: true, profileIds: new[] { ProfileId, OtherProfileId });

        await host.TryRunAsync(CancellationToken.None);

        _export.Calls.Should().Be(1, "the other profiles would fail the same way");
        var state = _state.Load();
        state.LastSuccessfulBackupAt.Should().BeNull();
        state.SignInRequired.Should().Be(CloudProvider.OneDrive);
        state.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Disconnected_account_is_notified_once_a_day_with_the_sign_in_message()
    {
        var host = CreateHost(hasPassphrase: true, cloudFolder: string.Empty, oneDriveAccount: "account-1",
            signedIn: false);

        await host.TryRunAsync(CancellationToken.None);
        _clock.Now = Now.AddMinutes(15);
        await host.TryRunAsync(CancellationToken.None);

        _notifications.Shown.Should().ContainSingle("the 15-minute ticks must not repeat the notification")
            .Which.Should().Be(("Notifications.Backup.Title", "Notifications.Backup.SignInRequired"));
        _state.Load().FailureNotifiedOn.Should().Be(DateOnly.FromDateTime(Now.UtcDateTime));
    }

    [Fact]
    public async Task Disconnected_Google_Drive_account_uses_its_own_message()
    {
        var host = CreateHost(hasPassphrase: true, cloudFolder: string.Empty, oneDriveAccount: "account-1",
            signedIn: false, provider: CloudProvider.GoogleDrive);

        await host.TryRunAsync(CancellationToken.None);

        _notifications.Shown.Should().ContainSingle()
            .Which.Body.Should().Be("Notifications.Backup.SignInRequired.GoogleDrive");
        _state.Load().SignInRequired.Should().Be(CloudProvider.GoogleDrive);
    }

    [Fact]
    public async Task Failure_that_persists_is_notified_again_the_next_day()
    {
        _storage.UploadFailure = new IOException("disk full");
        var host = CreateHost(hasPassphrase: true);

        await host.TryRunAsync(CancellationToken.None);
        _clock.Now = Now.AddDays(1);
        await host.TryRunAsync(CancellationToken.None);

        _notifications.Shown.Should().HaveCount(2);
        _notifications.Shown.Should().AllSatisfy(n => n.Body.Should().Be("Notifications.Backup.Failed"));
        _state.Load().FailureNotifiedOn.Should().Be(DateOnly.FromDateTime(Now.AddDays(1).UtcDateTime));
    }

    [Fact]
    public async Task Successful_tick_does_not_notify()
    {
        var host = CreateHost(hasPassphrase: true);

        await host.TryRunAsync(CancellationToken.None);

        _notifications.Shown.Should().BeEmpty();
        _state.Load().FailureNotifiedOn.Should().BeNull();
    }

    [Fact]
    public async Task Notification_that_cannot_be_shown_is_retried_on_the_next_tick()
    {
        _storage.UploadFailure = new IOException("disk full");
        _notifications.Failure = new InvalidOperationException("no tray");
        var host = CreateHost(hasPassphrase: true);

        await host.TryRunAsync(CancellationToken.None);
        _state.Load().FailureNotifiedOn.Should().BeNull();

        _notifications.Failure = null;
        _clock.Now = Now + AutomaticBackupHostedService.CloudRetryInterval;
        await host.TryRunAsync(CancellationToken.None);

        _notifications.Shown.Should().ContainSingle();
        _state.Load().LastError.Should().Be($"cloud {ProfileId}: disk full");
    }

    [Fact]
    public async Task Cloud_target_is_retried_the_same_day_after_a_new_sign_in()
    {
        var host = CreateHost(hasPassphrase: true, cloudFolder: string.Empty, oneDriveAccount: "account-1",
            signedIn: false, localEnabled: true);

        await host.TryRunAsync(CancellationToken.None);
        _local.Calls.Should().Be(1);
        _storage.Uploads.Should().BeEmpty();
        _state.Load().SignInRequired.Should().Be(CloudProvider.OneDrive);

        _accounts.SignedIn = _accounts.HasSession = true;
        _clock.Now = Now.AddMinutes(15);
        await host.TryRunAsync(CancellationToken.None);

        _local.Calls.Should().Be(1, "the local target is already done for the day");
        _storage.Uploads.Should().ContainSingle();
        var state = _state.Load();
        state.LastLocalBackupAt.Should().Be(Now);
        state.LastCloudBackupAt.Should().Be(Now.AddMinutes(15));
        state.SignInRequired.Should().BeNull();
        state.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Both_targets_done_for_the_day_skip_the_tick()
    {
        var host = CreateHost(hasPassphrase: true, localEnabled: true);

        await host.TryRunAsync(CancellationToken.None);
        _clock.Now = Now.AddMinutes(15);
        await host.TryRunAsync(CancellationToken.None);

        _local.Calls.Should().Be(1);
        _storage.Uploads.Should().ContainSingle();
    }

    [Fact]
    public async Task Manual_backup_does_not_close_the_automatic_local_day()
    {
        // What Settings saves after "Back up now" (current profile only).
        _state.Update(s => s with { LastSuccessfulBackupAt = Now.AddHours(-1), LastBackupFile = "manual.db" });
        var host = CreateHost(hasPassphrase: true, localEnabled: true);

        await host.TryRunAsync(CancellationToken.None);

        _local.Calls.Should().Be(1, "the automatic backup covers every profile");
    }

    [Fact]
    public async Task Tick_does_not_undo_state_saved_while_it_ran()
    {
        _storage.UploadFailure = new IOException("disk full");
        var manualAt = Now.AddMinutes(5);
        // Settings saves a manual backup while the export runs.
        _export.OnExport = () => _state.Update(s => s with
        {
            LastSuccessfulBackupAt = manualAt,
            LastBackupFile = "manual.db",
        });
        var host = CreateHost(hasPassphrase: true);

        await host.TryRunAsync(CancellationToken.None);

        var state = _state.Load();
        state.LastSuccessfulBackupAt.Should().Be(manualAt);
        state.LastBackupFile.Should().Be("manual.db");
        state.LastError.Should().Be($"cloud {ProfileId}: disk full");
    }

    [Fact]
    public async Task Failing_cloud_target_waits_the_retry_interval_before_exporting_again()
    {
        _storage.UploadFailure = new IOException("offline");
        var host = CreateHost(hasPassphrase: true);

        await host.TryRunAsync(CancellationToken.None);
        _clock.Now = Now.AddMinutes(15);
        await host.TryRunAsync(CancellationToken.None);
        _export.Calls.Should().Be(1, "each try costs an Argon2id export");

        _clock.Now = Now + AutomaticBackupHostedService.CloudRetryInterval;
        await host.TryRunAsync(CancellationToken.None);
        _export.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Successful_cloud_retry_clears_the_failure_time()
    {
        _storage.UploadFailure = new IOException("offline");
        var host = CreateHost(hasPassphrase: true);

        await host.TryRunAsync(CancellationToken.None);
        _state.Load().LastCloudFailureAt.Should().Be(Now);

        _storage.UploadFailure = null;
        _clock.Now = Now + AutomaticBackupHostedService.CloudRetryInterval;
        await host.TryRunAsync(CancellationToken.None);

        var state = _state.Load();
        state.LastCloudFailureAt.Should().BeNull();
        state.LastCloudBackupAt.Should().Be(_clock.Now);
        state.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Cloud_only_run_keeps_the_local_error_of_the_day()
    {
        _local.FailingProfileId = OtherProfileId;
        var host = CreateHost(hasPassphrase: true, cloudFolder: string.Empty, oneDriveAccount: "account-1",
            signedIn: false, localEnabled: true, profileIds: new[] { ProfileId, OtherProfileId });

        await host.TryRunAsync(CancellationToken.None);
        _accounts.SignedIn = _accounts.HasSession = true;
        _clock.Now = Now.AddMinutes(15);
        await host.TryRunAsync(CancellationToken.None);

        _local.Calls.Should().Be(2, "the local target ran once, for both profiles");
        _storage.Uploads.Should().HaveCount(2);
        var state = _state.Load();
        state.LocalError.Should().Be($"{OtherProfileId}: local failed");
        state.CloudError.Should().BeNull();
        state.LastError.Should().Be($"{OtherProfileId}: local failed");
    }

    private AutomaticBackupHostedService CreateHost(
        bool hasPassphrase, string? cloudFolder = null, string[]? profileIds = null,
        string? oneDriveAccount = null, bool signedIn = false, bool? hasSession = null,
        CloudProvider provider = CloudProvider.OneDrive, bool localEnabled = false)
    {
        _accounts.SignedIn = signedIn;
        _accounts.HasSession = hasSession ?? signedIn;
        var services = new ServiceCollection();
        services.AddSingleton<IBackupService>(_local);
        services.AddSingleton<IProfileRegistry>(new FixedProfileRegistry(profileIds ?? new[] { ProfileId }));
        services.AddSingleton<IArchiveStorage>(_storage);
        services.AddSingleton<ICloudBackupPassphraseStore>(new FakePassphraseStore(hasPassphrase));
        services.AddSingleton<IExportService>(_export);
        services.AddSingleton<ICurrentProfile, FakeCurrentProfile>();
        services.AddSingleton<ICloudAccountService>(_accounts);
        services.AddSingleton<IWindowsNotificationService>(_notifications);
        services.AddSingleton<ILocalizationService, KeyLocalization>();

        var settings = new BackupSettings
        {
            Enabled = localEnabled,
            Directory = localEnabled ? _cloudFolder : string.Empty,
            CloudFolderEnabled = true,
            CloudFolderDirectory = cloudFolder ?? _cloudFolder,
            CloudFolderRetention = 30,
            PreferredTime = "03:00",
            CloudProvider = oneDriveAccount is null ? null : provider,
            CloudAccountId = oneDriveAccount ?? string.Empty,
        };

        return new AutomaticBackupHostedService(
            services.BuildServiceProvider(),
            new StaticOptionsMonitor(settings),
            _state,
            _clock,
            NullLogger<AutomaticBackupHostedService>.Instance);
    }

    private const string ProfileId = "0123456789abcdef0123456789abcdef";
    private const string OtherProfileId = "fedcba9876543210fedcba9876543210";

    private sealed class FakeAccounts : ICloudAccountService
    {
        public bool SignedIn { get; set; }
        public bool HasSession { get; set; }

        public bool IsAvailable(CloudProvider provider) => true;

        public Task<CloudAccount> SignInAsync(CloudProvider provider, string? accountId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CloudAccount?> FindAsync(CloudProvider provider, string accountId, CancellationToken cancellationToken)
            => Task.FromResult(SignedIn ? new CloudAccount(provider, accountId, "user") : null);

        public Task<bool> HasSessionAsync(CloudProvider provider, string accountId, CancellationToken cancellationToken)
            => Task.FromResult(SignedIn && HasSession);
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class RecordingNotifications : IWindowsNotificationService
    {
        public List<(string Title, string Body)> Shown { get; } = new();
        public Exception? Failure { get; set; }

        public Task ShowAsync(string title, string body, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            Shown.Add((title, body));
            return Task.CompletedTask;
        }
    }

    // Returns the key, so the tests check which message was chosen.
    private sealed class KeyLocalization : ILocalizationService
    {
        public string CurrentLanguage => "en";
        public string Get(string key, params object?[] args) => key;
        public string GetIn(string languageCode, string key, params object?[] args) => key;
        public System.Globalization.CultureInfo CurrentCulture => System.Globalization.CultureInfo.InvariantCulture;
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
        public BackupState Update(Func<BackupState, BackupState> change) => _state = change(_state);
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
        public Action? OnExport { get; set; }

        public async Task<string> ExportAsync(
            ExportOptions options, char[] passphrase, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            ProfileIds.Add(options.ProfileId);
            OnExport?.Invoke();
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
        public bool DeleteAllLocalData() => throw new NotSupportedException();
        public void SetActiveProfileHint(string id) => throw new NotSupportedException();
        public void SetPin(string id, string? pin) => throw new NotSupportedException();
        public bool VerifyPin(string id, string pin) => throw new NotSupportedException();
        public void SetRole(string id, ProfileRole role) => throw new NotSupportedException();
        public ProfilePinHash? GetPinHash(string id) => null;
        public void SetPinHash(string id, ProfilePinHash? pin) => throw new NotSupportedException();
        public bool HasPin(string id) => false;
        public void Register(string id, string displayName, ProfileRole role, ProfilePinHash? pin) => throw new NotSupportedException();
    }

    // The local raw-DB target, counted; off unless a test enables it.
    private sealed class LocalBackupService : IBackupService
    {
        public string DatabasePath => string.Empty;
        public int Calls { get; private set; }
        public string? FailingProfileId { get; set; }

        public Task<string> ExportProfileAsync(
            string profileId, string destinationDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            if (profileId == FailingProfileId) throw new IOException("local failed");
            return Task.FromResult(Path.Combine(destinationDirectory, profileId + ".db"));
        }

        public Task<int> PruneOldBackupsAsync(
            string directory, int retentionDays, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task ImportProfileAsync(
            string profileId, string sourceFilePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> PruneCloudFolderAsync(
            IArchiveStorage storage, int retentionDays, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
