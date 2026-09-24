using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Export;

// Produces an encrypted .mrz archive of the current profile
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
    private readonly ICurrentProfile _currentProfile;
    private readonly IBackupService _backupService;
    private readonly IArchiveCipher _cipher;
    private readonly ISmtpCredentialStore _credentialStore;
    private readonly TimeProvider _clock;
    private readonly ILogger<ExportService> _log;
    private readonly string _sharedDirectory;

    public ExportService(
        ICurrentProfile currentProfile,
        IBackupService backupService,
        IArchiveCipher cipher,
        ISmtpCredentialStore credentialStore,
        TimeProvider clock,
        ILogger<ExportService> log)
        : this(currentProfile, backupService, cipher, credentialStore, clock, log,
            AppDataPaths.GetAppDataDirectory())
    {
    }

    // Test overload: lets a test point the shared-settings reader at a
    // temporary folder instead of %LOCALAPPDATA%.
    internal ExportService(
        ICurrentProfile currentProfile,
        IBackupService backupService,
        IArchiveCipher cipher,
        ISmtpCredentialStore credentialStore,
        TimeProvider clock,
        ILogger<ExportService> log,
        string sharedDirectory)
    {
        _currentProfile = currentProfile;
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
        if (passphrase.Length < ExportFormat.MinPassphraseLength)
        {
            throw new ExportValidationException(
                ExportValidationReason.PassphraseTooShort,
                $"The passphrase must be at least {ExportFormat.MinPassphraseLength} characters.");
        }

        // The admin-only all-profiles scope is deferred to a follow-up
        // (§3.5, §12 item 3); the first cut ships profile scope only.
        if (options.Scope != ExportScope.Profile)
        {
            throw new ExportValidationException(
                ExportValidationReason.ScopeNotPermitted,
                "Only single-profile export is supported in this version.");
        }

        progress?.Report(0);

        var scratchDirectory = Path.Combine(
            Path.GetTempPath(), "MedReminder-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDirectory);

        byte[]? key = null;
        try
        {
            // Step 2: torn-write-free snapshot via the online-backup API.
            var snapshotPath = await _backupService.ExportProfileAsync(
                _currentProfile.Id, scratchDirectory, cancellationToken);

            // Step 3: read every entity from the snapshot into DTOs.
            var payload = await ReadPayloadAsync(snapshotPath, cancellationToken);

            // Step 4: collect the opt-in shared files.
            var settingsFiles = new ExportSettingsFiles(_sharedDirectory);
            payload.NotificationSettings =
                settingsFiles.ReadNotificationSettings(_currentProfile.NotificationSettingsPath);

            var includes = new ManifestIncludes();
            CollectSharedFiles(options, settingsFiles, payload, includes);

            progress?.Report(50);

            // Step 5: serialize payload.json (UTF-8, no BOM, camelCase).
            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(
                payload, ExportJson.Options);

            // Step 6: derive the archive key from a fresh random salt.
            var salt = RandomNumberGenerator.GetBytes(ExportFormat.SaltSizeBytes);
            var kdfParams = Argon2Params.Default;
            key = _cipher.DeriveKey(passphrase, salt, kdfParams);

            // Re-encrypt the SMTP password (if opted in) with the same
            // archive key, now that it is available (§3.4).
            if (options is { IncludeSmtpSettings: true, IncludeSmtpPassword: true })
            {
                var secret = EncryptSmtpPassword(key);
                if (secret is not null)
                {
                    payload.Shared.SmtpPasswordEncrypted = secret;
                    includes.SmtpCredential = true;
                    // The password lives in payload.json, so re-serialize
                    // after adding it.
                    payloadBytes = JsonSerializer.SerializeToUtf8Bytes(
                        payload, ExportJson.Options);
                }
            }

            // Step 7: AES-GCM-encrypt the payload.
            var (nonce, tag, ciphertext) = _cipher.Encrypt(key, payloadBytes);

            // Step 8: SHA-256 over the plaintext for the manifest hash.
            var payloadHash = SHA256.HashData(payloadBytes);

            // Step 9: build the manifest.
            var manifest = new ExportManifest
            {
                AppVersion = ResolveAppVersion(),
                CreatedAtUtc = _clock.GetUtcNow(),
                Scope = "profile",
                ProfileId = _currentProfile.Id,
                Kdf = new ManifestKdf
                {
                    Iterations = kdfParams.Iterations,
                    MemoryKiB = kdfParams.MemoryKiB,
                    Parallelism = kdfParams.Parallelism,
                    SaltBase64 = Convert.ToBase64String(salt),
                },
                Cipher = new ManifestCipher
                {
                    NonceBase64 = Convert.ToBase64String(nonce),
                    TagBase64 = Convert.ToBase64String(tag),
                },
                Payload = new ManifestPayload
                {
                    Sha256Base64 = Convert.ToBase64String(payloadHash),
                    SizeBytes = payloadBytes.Length,
                },
                Includes = includes,
            };

            // Step 10: write the ZIP (manifest.json cleartext + payload.enc).
            WriteArchive(options.DestinationPath, manifest, ciphertext);

            progress?.Report(100);
            _log.LogInformation(
                "Exported profile {ProfileId} to an encrypted archive.", _currentProfile.Id);
            return options.DestinationPath;
        }
        finally
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
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

    private async Task<ExportPayload> ReadPayloadAsync(
        string snapshotPath, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(AppDataPaths.BuildSqliteConnectionString(snapshotPath))
            .Options;

        await using var db = new MedReminderDbContext(options);

        var payload = new ExportPayload
        {
            Profile = new ExportedProfileInfo
            {
                Id = _currentProfile.Id,
                DisplayName = _currentProfile.DisplayName,
                Role = _currentProfile.Role.ToString(),
                CreatedAt = _clock.GetUtcNow(),
            },
        };

        // Ordered by Id so the produced payload is stable and diffable;
        // AsNoTracking because this context is read-only.
        payload.Medicines = (await db.Medicines.AsNoTracking()
            .OrderBy(m => m.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.StockMovements = (await db.StockMovements.AsNoTracking()
            .OrderBy(m => m.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.MedicationScheduleHistory = (await db.MedicationScheduleHistories.AsNoTracking()
            .OrderBy(s => s.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.MedicationAdministrationSlots = (await db.MedicationAdministrationSlots.AsNoTracking()
            .OrderBy(s => s.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.MedicationSuspensions = (await db.MedicationSuspensions.AsNoTracking()
            .OrderBy(s => s.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.MedicationIntakes = (await db.MedicationIntakes.AsNoTracking()
            .OrderBy(i => i.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.NotificationEvents = (await db.NotificationEvents.AsNoTracking()
            .OrderBy(e => e.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.DoseReminderEvents = (await db.DoseReminderEvents.AsNoTracking()
            .OrderBy(e => e.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();

        return payload;
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

    // DPAPI-decrypt the SMTP password into a transient buffer, then
    // AES-GCM-encrypt it with the archive key (§3.4). The plaintext
    // byte buffer is zeroed before returning. Returns null when no
    // password is stored.
    private ExportedProtectedSecret? EncryptSmtpPassword(byte[] key)
    {
        if (!_credentialStore.HasPassword) return null;

        var plaintext = _credentialStore.GetPassword();
        if (string.IsNullOrEmpty(plaintext)) return null;

        var passwordBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var (nonce, tag, ciphertext) = _cipher.Encrypt(key, passwordBytes);
            return new ExportedProtectedSecret
            {
                NonceBase64 = Convert.ToBase64String(nonce),
                TagBase64 = Convert.ToBase64String(tag),
                CiphertextBase64 = Convert.ToBase64String(ciphertext),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static void WriteArchive(
        string destinationPath, ExportManifest manifest, byte[] ciphertext)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new System.IO.Compression.ZipArchive(
            stream, System.IO.Compression.ZipArchiveMode.Create);

        var manifestEntry = archive.CreateEntry(
            ExportFormat.ManifestEntryName,
            System.IO.Compression.CompressionLevel.Optimal);
        using (var manifestStream = manifestEntry.Open())
        {
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                manifest, ExportJson.Options);
            manifestStream.Write(manifestBytes);
        }

        var payloadEntry = archive.CreateEntry(
            ExportFormat.PayloadEntryName,
            System.IO.Compression.CompressionLevel.Optimal);
        using var payloadStream = payloadEntry.Open();
        payloadStream.Write(ciphertext);
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

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            // A leftover temp snapshot is harmless; do not fail the
            // export over it. Log without any payload detail.
            _log.LogWarning(ex, "Could not delete the temporary export snapshot directory.");
        }
    }
}
