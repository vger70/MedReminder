using System.Data;
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

// Reads an encrypted .mrz archive and applies it to the current
// profile in Overwrite mode (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md
// §4.2). Merge is out of scope (§1.3).
//
// The write strategy mirrors BackupService.ImportProfileAsync: the new
// state is materialised into a temporary SQLite file (fresh schema +
// inserted rows in a single transaction), then the live DB is released
// (ClearAllPools + close the DbContext connection), the current file is
// renamed to <db>.bak-<timestamp> as the pre-import safety copy (§4.2
// step 8), and the temp file is moved into place. A restart follows
// (surfaced by the UI, §4.2 step 11) so the hosted services pick up the
// swapped DB cleanly.
//
// Security (CLAUDE.md §9): the passphrase, the derived key and the
// payload plaintext are never logged; the derived key and the
// intermediate SMTP-password buffer are zeroed after use.
[SupportedOSPlatform("windows")]
internal sealed class ImportService : IImportService
{
    private readonly ICurrentProfile _currentProfile;
    private readonly MedReminderDbContext _db;
    private readonly IArchiveCipher _cipher;
    private readonly ICredentialProtector _credentialProtector;
    private readonly TimeProvider _clock;
    private readonly ILogger<ImportService> _log;
    private readonly string _sharedDirectory;

    public ImportService(
        ICurrentProfile currentProfile,
        MedReminderDbContext db,
        IArchiveCipher cipher,
        ICredentialProtector credentialProtector,
        TimeProvider clock,
        ILogger<ImportService> log)
        : this(currentProfile, db, cipher, credentialProtector, clock, log,
            AppDataPaths.GetAppDataDirectory())
    {
    }

    // Test overload: shared-settings restores are written under the
    // given directory instead of %LOCALAPPDATA%.
    internal ImportService(
        ICurrentProfile currentProfile,
        MedReminderDbContext db,
        IArchiveCipher cipher,
        ICredentialProtector credentialProtector,
        TimeProvider clock,
        ILogger<ImportService> log,
        string sharedDirectory)
    {
        _currentProfile = currentProfile;
        _db = db;
        _cipher = cipher;
        _credentialProtector = credentialProtector;
        _clock = clock;
        _log = log;
        _sharedDirectory = sharedDirectory;
    }

    public async Task<ExportManifest> ReadManifestAsync(
        string archivePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var (manifest, _) = OpenAndValidateManifest(archivePath);
        await Task.CompletedTask;
        return manifest;
    }

    public async Task ImportAsync(
        string archivePath,
        char[] passphrase,
        ImportOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(passphrase);
        ArgumentNullException.ThrowIfNull(options);

        progress?.Report(0);

        // Steps 1-2: open the ZIP, parse and version-check the manifest.
        var (manifest, ciphertext) = OpenAndValidateManifest(archivePath);

        byte[]? key = null;
        try
        {
            // Step 3: derive the key from the manifest's KDF parameters.
            var kdfParams = new Argon2Params
            {
                Iterations = manifest.Kdf.Iterations,
                MemoryKiB = manifest.Kdf.MemoryKiB,
                Parallelism = manifest.Kdf.Parallelism,
            };
            var salt = DecodeBase64(manifest.Kdf.SaltBase64, "kdf.saltBase64");
            key = _cipher.DeriveKey(passphrase, salt, kdfParams);

            // Step 4: decrypt. A tag mismatch is the wrong-passphrase
            // surface (§4.4).
            var nonce = DecodeBase64(manifest.Cipher.NonceBase64, "cipher.nonceBase64");
            var tag = DecodeBase64(manifest.Cipher.TagBase64, "cipher.tagBase64");
            byte[] plaintext;
            try
            {
                plaintext = _cipher.Decrypt(key, nonce, tag, ciphertext);
            }
            catch (CryptographicException ex)
            {
                throw new ImportFailedException(
                    ImportFailureReason.WrongPassphrase,
                    "The passphrase does not match this file.", ex);
            }

            // Step 5: verify the payload hash.
            var expectedHash = DecodeBase64(
                manifest.Payload.Sha256Base64, "payload.sha256Base64");
            var actualHash = SHA256.HashData(plaintext);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw new ImportFailedException(
                    ImportFailureReason.Corrupt,
                    "The export file is damaged and cannot be imported.");
            }

            // Step 6: parse payload.json and check the schema version.
            ExportPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<ExportPayload>(plaintext, ExportJson.Options);
            }
            catch (JsonException ex)
            {
                throw new ImportFailedException(
                    ImportFailureReason.Corrupt,
                    "The export file is damaged and cannot be imported.", ex);
            }

            if (payload is null)
            {
                throw new ImportFailedException(
                    ImportFailureReason.Corrupt,
                    "The export file is damaged and cannot be imported.");
            }

            if (payload.SchemaVersion > ExportFormat.CurrentSchemaVersion)
            {
                throw new ImportFailedException(
                    ImportFailureReason.UnsupportedVersion,
                    "This export was produced by a newer version of MedReminder. "
                    + "Update the app and try again.");
            }

            progress?.Report(40);

            // Steps 8-9: build the new DB in a temp file, then swap it
            // in with a pre-import safety backup.
            await ApplyPayloadAsync(payload, cancellationToken);

            progress?.Report(80);

            // Step 10: apply the opt-in shared-file restores.
            ApplySharedRestores(payload, manifest, options, key);

            progress?.Report(100);
            _log.LogInformation(
                "Imported an encrypted archive into profile {ProfileId}.", _currentProfile.Id);
        }
        finally
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    // Opens the archive, reads manifest.json and the payload.enc bytes,
    // and enforces the format identifier and version guards (§4.3).
    private static (ExportManifest Manifest, byte[] Ciphertext) OpenAndValidateManifest(
        string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            throw new ImportFailedException(
                ImportFailureReason.Corrupt,
                "The export file is damaged and cannot be imported.");
        }

        System.IO.Compression.ZipArchive archive;
        try
        {
            archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            throw new ImportFailedException(
                ImportFailureReason.Corrupt,
                "The export file is damaged and cannot be imported.", ex);
        }

        using (archive)
        {
            var manifestEntry = archive.GetEntry(ExportFormat.ManifestEntryName);
            var payloadEntry = archive.GetEntry(ExportFormat.PayloadEntryName);
            if (manifestEntry is null || payloadEntry is null)
            {
                throw new ImportFailedException(
                    ImportFailureReason.Corrupt,
                    "The export file is damaged and cannot be imported.");
            }

            ExportManifest? manifest;
            try
            {
                using var manifestStream = manifestEntry.Open();
                manifest = JsonSerializer.Deserialize<ExportManifest>(
                    manifestStream, ExportJson.Options);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                throw new ImportFailedException(
                    ImportFailureReason.Corrupt,
                    "The export file is damaged and cannot be imported.", ex);
            }

            if (manifest is null
                || !string.Equals(manifest.Format, ExportFormat.FormatIdentifier, StringComparison.Ordinal))
            {
                throw new ImportFailedException(
                    ImportFailureReason.Corrupt,
                    "The export file is damaged and cannot be imported.");
            }

            if (manifest.FormatVersion > ExportFormat.CurrentFormatVersion)
            {
                throw new ImportFailedException(
                    ImportFailureReason.UnsupportedVersion,
                    "This export was produced by a newer version of MedReminder. "
                    + "Update the app and try again.");
            }

            byte[] ciphertext;
            try
            {
                using var payloadStream = payloadEntry.Open();
                using var buffer = new MemoryStream();
                payloadStream.CopyTo(buffer);
                ciphertext = buffer.ToArray();
            }
            catch (InvalidDataException ex)
            {
                throw new ImportFailedException(
                    ImportFailureReason.Corrupt,
                    "The export file is damaged and cannot be imported.", ex);
            }

            return (manifest, ciphertext);
        }
    }

    // Materialises the payload into a fresh temp DB (schema + rows in a
    // single transaction), then swaps it into the profile's DB path
    // with a pre-import safety backup. FK-safe insert order: the parent
    // Medicine rows first, then every dependent table.
    private async Task ApplyPayloadAsync(ExportPayload payload, CancellationToken cancellationToken)
    {
        var target = Path.Combine(
            AppDataPaths.GetProfileDataDirectory(_currentProfile.Id),
            AppDataPaths.DatabaseFileName);

        var tempDbPath = Path.Combine(
            Path.GetDirectoryName(target)!,
            $"medreminder.import-{Guid.NewGuid():N}.db");

        try
        {
            await BuildTempDatabaseAsync(tempDbPath, payload, cancellationToken);

            // Release the live DB so the file can be replaced on Windows.
            SqliteConnection.ClearAllPools();
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Closed)
            {
                conn.Close();
            }

            // Step 8: pre-import safety copy of the current DB.
            if (File.Exists(target))
            {
                var backupName = $"{target}.bak-{_clock.GetUtcNow():yyyyMMddHHmmss}";
                File.Move(target, backupName, overwrite: false);
            }

            File.Move(tempDbPath, target, overwrite: false);

            // Drop any stale WAL / SHM side files from the old DB so the
            // swapped-in database is not shadowed by them.
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var side = target + suffix;
                if (File.Exists(side))
                {
                    try { File.Delete(side); } catch { /* best effort */ }
                }
            }
        }
        finally
        {
            if (File.Exists(tempDbPath))
            {
                try { File.Delete(tempDbPath); } catch { /* best effort */ }
            }
        }
    }

    private static async Task BuildTempDatabaseAsync(
        string tempDbPath, ExportPayload payload, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(AppDataPaths.BuildSqliteConnectionString(tempDbPath))
            .Options;

        await using var db = new MedReminderDbContext(options);

        // Fresh schema for the current model. The archive's schemaVersion
        // has already been checked as <= current; older archives simply
        // omit newer columns, which take their defaults (§4.3).
        await db.Database.EnsureCreatedAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Parents first (Medicines), then dependents — every dependent
        // table FKs back to Medicines with ON DELETE RESTRICT.
        db.Medicines.AddRange(payload.Medicines.Select(ExportMapper.ToEntity));
        db.StockMovements.AddRange(payload.StockMovements.Select(ExportMapper.ToEntity));
        db.MedicationScheduleHistories.AddRange(
            payload.MedicationScheduleHistory.Select(ExportMapper.ToEntity));
        db.MedicationAdministrationSlots.AddRange(
            payload.MedicationAdministrationSlots.Select(ExportMapper.ToEntity));
        db.MedicationSuspensions.AddRange(
            payload.MedicationSuspensions.Select(ExportMapper.ToEntity));
        db.MedicationIntakes.AddRange(payload.MedicationIntakes.Select(ExportMapper.ToEntity));
        db.NotificationEvents.AddRange(payload.NotificationEvents.Select(ExportMapper.ToEntity));
        db.DoseReminderEvents.AddRange(payload.DoseReminderEvents.Select(ExportMapper.ToEntity));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void ApplySharedRestores(
        ExportPayload payload, ExportManifest manifest, ImportOptions options, byte[] key)
    {
        var settingsFiles = new ExportSettingsFiles(_sharedDirectory);

        // Per-profile notification settings travel with the profile
        // (§3.2); restore whenever the archive carried them.
        if (payload.NotificationSettings is not null)
        {
            WriteNotificationSettings(payload.NotificationSettings);
        }

        if (options.RestoreUserSettings && payload.Shared.UserSettings is not null)
        {
            settingsFiles.WriteUserSettings(payload.Shared.UserSettings);
        }

        if (options.RestoreBackupSettings && payload.Shared.BackupSettings is not null)
        {
            settingsFiles.WriteBackupSettings(payload.Shared.BackupSettings);
        }

        if (options.RestoreSmtpSettings && payload.Shared.SmtpSettings is not null)
        {
            settingsFiles.WriteSmtpSettings(payload.Shared.SmtpSettings);
        }

        // SMTP password: AES-GCM-decrypt with the archive key, then
        // DPAPI-re-encrypt on the current Windows account (§4.2 step 10).
        if (options.RestoreSmtpPassword && payload.Shared.SmtpPasswordEncrypted is not null)
        {
            RestoreSmtpPassword(payload.Shared.SmtpPasswordEncrypted, key);
        }
    }

    private void RestoreSmtpPassword(ExportedProtectedSecret secret, byte[] key)
    {
        var nonce = DecodeBase64(secret.NonceBase64, "smtpPasswordEncrypted.nonceBase64");
        var tag = DecodeBase64(secret.TagBase64, "smtpPasswordEncrypted.tagBase64");
        var ciphertext = DecodeBase64(
            secret.CiphertextBase64, "smtpPasswordEncrypted.ciphertextBase64");

        byte[] plaintextBytes;
        try
        {
            plaintextBytes = _cipher.Decrypt(key, nonce, tag, ciphertext);
        }
        catch (CryptographicException ex)
        {
            // The archive key already decrypted the payload, so a failure
            // here means a tampered secret block — treat as corrupt.
            throw new ImportFailedException(
                ImportFailureReason.Corrupt,
                "The export file is damaged and cannot be imported.", ex);
        }

        try
        {
            var password = Encoding.UTF8.GetString(plaintextBytes);
            var ciphertextForDpapi = _credentialProtector.Protect(password);
            var credentialsPath = Path.Combine(
                _sharedDirectory, AppDataPaths.CredentialsFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(credentialsPath)!);
            File.WriteAllText(credentialsPath, ciphertextForDpapi);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    private void WriteNotificationSettings(ExportedNotificationSettings settings)
    {
        var payload = new Dictionary<string, ExportedNotificationSettings>
        {
            ["Notifications"] = settings,
        };
        var path = _currentProfile.NotificationSettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(path, json);
    }

    private static byte[] DecodeBase64(string value, string field)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new ImportFailedException(
                ImportFailureReason.Corrupt,
                $"The export file is damaged and cannot be imported.", ex);
        }
    }
}
