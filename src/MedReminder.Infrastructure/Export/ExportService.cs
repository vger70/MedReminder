using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Export;

// Produces an encrypted .mrz archive of one profile — the active one
// by default, or ExportOptions.ProfileId
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.1). The algorithm
// follows §4.1 step by step: validate the passphrase, snapshot the DB
// via the SQLite online-backup API (BackupService), read every entity
// through a temporary read-only EF Core context, collect the opt-in
// shared files, serialize / hash / encrypt the payload, and write the
// ZIP.
//
// Security (CLAUDE.md §9): the passphrase, the derived key and the
// payload plaintext are never logged; the derived key and the
// intermediate SMTP-password buffer are zeroed after use.
[SupportedOSPlatform("windows")]
internal sealed class ExportService : IExportService
{
    // Same file name CurrentProfile uses for the per-profile recipient.
    private const string ProfileNotificationSettingsFileName = "notifications.settings.json";

    private readonly ICurrentProfile _currentProfile;
    private readonly IProfileRegistry? _profileRegistry;
    private readonly IBackupService _backupService;
    private readonly IArchiveCipher _cipher;
    private readonly ISmtpCredentialStore _credentialStore;
    private readonly TimeProvider _clock;
    private readonly ILogger<ExportService> _log;
    private readonly string _sharedDirectory;
    private readonly string _scratchRoot;

    public ExportService(
        ICurrentProfile currentProfile,
        IBackupService backupService,
        IArchiveCipher cipher,
        ISmtpCredentialStore credentialStore,
        TimeProvider clock,
        ILogger<ExportService> log,
        IProfileRegistry profileRegistry)
        : this(currentProfile, backupService, cipher, credentialStore, clock, log,
            AppDataPaths.GetAppDataDirectory(), profileRegistry)
    {
    }

    // Test overload: lets a test point the shared-settings reader at a
    // temporary folder instead of %LOCALAPPDATA%, and the scratch folder
    // of the snapshot at a folder of its own instead of %TEMP%, which
    // other tests share.
    internal ExportService(
        ICurrentProfile currentProfile,
        IBackupService backupService,
        IArchiveCipher cipher,
        ISmtpCredentialStore credentialStore,
        TimeProvider clock,
        ILogger<ExportService> log,
        string sharedDirectory,
        IProfileRegistry? profileRegistry = null,
        string? scratchRoot = null)
    {
        _scratchRoot = scratchRoot ?? Path.GetTempPath();
        _currentProfile = currentProfile;
        _profileRegistry = profileRegistry;
        _backupService = backupService;
        _cipher = cipher;
        _credentialStore = credentialStore;
        _clock = clock;
        _log = log;
        _sharedDirectory = sharedDirectory;
    }

    public async Task<string> ExportAsync(
        ExportOptions options,
        char[] passphrase,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(passphrase);

        // Step 1: validate the passphrase before touching the disk, so
        // a rejected export never leaves a partial file (§4.5).
        ArchiveWriter.ValidatePassphrase(passphrase);

        // The admin-only all-profiles scope is deferred to a follow-up
        // (§3.5, §12 item 3); the first cut ships profile scope only.
        if (options.Scope != ExportScope.Profile)
        {
            throw new ExportValidationException(
                ExportValidationReason.ScopeNotPermitted,
                "Only single-profile export is supported in this version.");
        }

        var target = ResolveTarget(options);

        progress?.Report(0);

        var scratchDirectory = Path.Combine(
            _scratchRoot, "MedReminder-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDirectory);

        byte[]? smtpPassword = null;
        try
        {
            // Step 2: torn-write-free snapshot via the online-backup API.
            var snapshotPath = await _backupService.ExportProfileAsync(
                target.Id, scratchDirectory, cancellationToken);

            // Step 3: read every entity from the snapshot into DTOs.
            var payload = await ReadPayloadAsync(snapshotPath, target, cancellationToken);

            // Step 4: collect the opt-in shared files.
            var settingsFiles = new ExportSettingsFiles(_sharedDirectory);
            payload.NotificationSettings =
                ExportSettingsFiles.ReadNotificationSettings(target.NotificationSettingsPath);

            var includes = new ManifestIncludes();
            CollectSharedFiles(options, settingsFiles, payload, includes);

            progress?.Report(50);

            // The SMTP password (if opted in) is re-encrypted with the
            // archive key by the writer (§3.4).
            if (options is { IncludeSmtpSettings: true, IncludeSmtpPassword: true })
            {
                smtpPassword = ReadSmtpPassword();
            }

            // C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.6):
            // automatic-scheduled cloud snapshots carry an origin marker
            // and a HASHED host name. User-triggered C.3 exports leave
            // both fields null so the archive stays byte-compatible with
            // pre-C.3+ readers.
            var content = new ArchiveContent
            {
                Payload = payload,
                ProfileId = target.Id,
                AppVersion = ResolveAppVersion(),
                CreatedAtUtc = _clock.GetUtcNow(),
                Includes = includes,
                SmtpPassword = smtpPassword,
                Source = options.AutomaticSource ? AutomaticArchiveSource.Source : null,
                Device = options.AutomaticSource
                    ? AutomaticArchiveSource.Device(Environment.MachineName, target.Id)
                    : null,
            };

            // Steps 5-10: serialize, derive the key, encrypt, hash, build
            // the manifest and write the ZIP (ArchiveWriter).
            WriteArchive(options.DestinationPath, content, passphrase);

            progress?.Report(100);
            _log.LogInformation(
                "Exported profile {ProfileId} to an encrypted archive.", target.Id);
            return options.DestinationPath;
        }
        finally
        {
            if (smtpPassword is not null)
            {
                CryptographicOperations.ZeroMemory(smtpPassword);
            }

            // Step 11: delete the temp DB snapshot and scratch folder.
            // Release pooled SQLite connections first — both the backup
            // destination and the read-only snapshot context hold the
            // file open until the pool is cleared, which would otherwise
            // leave the scratch .db locked on Windows.
            SqliteConnection.ClearAllPools();
            TryDeleteDirectory(scratchDirectory);
        }
    }

    // Identity and paths of the profile being exported.
    private sealed record ExportTarget(
        string Id, string DisplayName, ProfileRole Role, string NotificationSettingsPath);

    private ExportTarget ResolveTarget(ExportOptions options)
    {
        var requested = options.ProfileId;
        if (string.IsNullOrWhiteSpace(requested)
            || string.Equals(requested, _currentProfile.Id, StringComparison.OrdinalIgnoreCase))
        {
            return new ExportTarget(
                _currentProfile.Id,
                _currentProfile.DisplayName,
                _currentProfile.Role,
                _currentProfile.NotificationSettingsPath);
        }

        if (!options.AutomaticSource && !_currentProfile.IsAdmin)
        {
            throw new ExportValidationException(
                ExportValidationReason.ScopeNotPermitted,
                "Only an admin profile can export another profile.");
        }

        var profile = _profileRegistry?.GetById(requested)
            ?? throw new ExportValidationException(
                ExportValidationReason.ProfileNotFound,
                $"Profile '{requested}' is not registered.");

        return new ExportTarget(
            profile.Id,
            profile.DisplayName,
            profile.Role,
            Path.Combine(
                AppDataPaths.GetProfileDataDirectory(profile.Id),
                ProfileNotificationSettingsFileName));
    }

    private async Task<ExportPayload> ReadPayloadAsync(
        string snapshotPath, ExportTarget target, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(AppDataPaths.BuildSqliteConnectionString(snapshotPath))
            .Options;

        await using var db = new MedReminderDbContext(options);

        var profile = new ExportedProfileInfo
        {
            Id = target.Id,
            DisplayName = target.DisplayName,
            Role = target.Role.ToString(),
            CreatedAt = _clock.GetUtcNow(),
        };
        return await ProfilePayloadReader.ReadAsync(db, profile, cancellationToken);
    }

    private static void CollectSharedFiles(
        ExportOptions options,
        ExportSettingsFiles settingsFiles,
        ExportPayload payload,
        ManifestIncludes includes)
    {
        if (options.IncludeUserSettings)
        {
            var user = settingsFiles.ReadUserSettings();
            if (user is not null)
            {
                payload.Shared.UserSettings = user;
                includes.UserSettings = true;
            }
        }

        if (options.IncludeBackupSettings)
        {
            var backup = settingsFiles.ReadBackupSettings();
            if (backup is not null)
            {
                payload.Shared.BackupSettings = backup;
                includes.BackupSettings = true;
            }
        }

        if (options.IncludeSmtpSettings)
        {
            var smtp = settingsFiles.ReadSmtpSettings();
            if (smtp is not null)
            {
                payload.Shared.SmtpSettings = smtp;
                includes.SmtpSettings = true;
            }
        }
    }

    // DPAPI-decrypt the SMTP password into a transient UTF-8 buffer,
    // which the caller zeroes after the archive is written (§3.4).
    // Returns null when no password is stored.
    private byte[]? ReadSmtpPassword()
    {
        if (!_credentialStore.HasPassword) return null;

        var plaintext = _credentialStore.GetPassword();
        return string.IsNullOrEmpty(plaintext) ? null : Encoding.UTF8.GetBytes(plaintext);
    }

    private void WriteArchive(string destinationPath, ArchiveContent content, char[] passphrase)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        ArchiveWriter.Write(stream, content, passphrase, _cipher);
    }

    private static string ResolveAppVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip any "+<git sha>" build-metadata suffix.
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
    }

    // Windows can keep a just-closed file locked for a short while (an
    // antivirus or the search indexer scanning the new .db), so the
    // deletion is retried a few times before giving up.
    private const int DeleteAttempts = 5;

    private void TryDeleteDirectory(string directory)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
                return;
            }
            catch (Exception ex) when (attempt < DeleteAttempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100 * attempt);
            }
            catch (Exception ex)
            {
                // A leftover temp snapshot is harmless; do not fail the
                // export over it. Log without any payload detail.
                _log.LogWarning(ex, "Could not delete the temporary export snapshot directory.");
                return;
            }
        }
    }
}
