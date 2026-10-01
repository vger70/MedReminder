using System.Data;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using MedReminder.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Export;

// Reads an encrypted .mrz archive and applies it to the current
// profile in Overwrite mode (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md
// §4.2). Merge is out of scope (§1.3).
//
// Windows shell over the platform-neutral ArchiveReader (steps 1-6)
// and ProfileDatabaseBuilder (step 9); this class keeps the file swap,
// the DPAPI rewrap of the SMTP password and the shared settings files.
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
    private readonly IArchiveReader _reader;
    private readonly ICredentialProtector _credentialProtector;
    private readonly TimeProvider _clock;
    private readonly IDatabaseExclusiveAccess _exclusiveAccess;
    private readonly ILogger<ImportService> _log;
    private readonly string _sharedDirectory;

    public ImportService(
        ICurrentProfile currentProfile,
        MedReminderDbContext db,
        IArchiveCipher cipher,
        ICredentialProtector credentialProtector,
        TimeProvider clock,
        IDatabaseExclusiveAccess exclusiveAccess,
        ILogger<ImportService> log)
        : this(currentProfile, db, cipher, credentialProtector, clock, exclusiveAccess, log,
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
        IDatabaseExclusiveAccess exclusiveAccess,
        ILogger<ImportService> log,
        string sharedDirectory)
    {
        _currentProfile = currentProfile;
        _db = db;
        _reader = new ArchiveReader(cipher);
        _credentialProtector = credentialProtector;
        _clock = clock;
        _exclusiveAccess = exclusiveAccess;
        _log = log;
        _sharedDirectory = sharedDirectory;
    }

    public async Task<ExportManifest> ReadManifestAsync(
        string archivePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        await using var stream = OpenArchive(archivePath);
        return _reader.ReadManifest(stream);
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

        // Steps 1-6: open, validate, decrypt and parse (ArchiveReader).
        DecryptedArchive archive;
        await using (var stream = OpenArchive(archivePath))
        {
            archive = _reader.Decrypt(stream, passphrase);
        }

        using (archive)
        {
            progress?.Report(40);

            // Steps 8-9: build the new DB in a temp file, then swap it
            // in with a pre-import safety backup.
            await ApplyPayloadAsync(archive.Payload, cancellationToken);

            progress?.Report(80);

            // Step 10: apply the opt-in shared-file restores.
            ApplySharedRestores(archive, options);

            progress?.Report(100);
            _log.LogInformation(
                "Imported an encrypted archive into profile {ProfileId}.", _currentProfile.Id);
        }
    }

    private static FileStream OpenArchive(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            throw new ImportFailedException(
                ImportFailureReason.Corrupt,
                "The export file is damaged and cannot be imported.");
        }

        try
        {
            return File.OpenRead(archivePath);
        }
        catch (IOException ex)
        {
            throw new ImportFailedException(
                ImportFailureReason.Corrupt,
                "The export file is damaged and cannot be imported.", ex);
        }
    }

    // Materialises the payload into a fresh temp DB (schema + rows in a
    // single transaction, ProfileDatabaseBuilder), then swaps it into the
    // profile's DB path with a pre-import safety backup.
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
            await ProfileDatabaseBuilder.BuildAsync(tempDbPath, payload, cancellationToken, _clock);

            // B.1 Phase 3d (§5.7): on a synced profile the import starts a
            // new sync generation. Marked before the swap, so no sync run
            // applies the old generation to the imported data.
            JsonSyncSettingsStore.MarkResetPending(Path.GetDirectoryName(target)!);

            // Step 8: pre-import safety copy of the current DB, then the
            // swap (ProfileDatabaseSwap).
            await ProfileDatabaseSwap.ReplaceAsync(
                _exclusiveAccess, _db, target, tempDbPath, _clock, cancellationToken);
        }
        finally
        {
            if (File.Exists(tempDbPath))
            {
                try { File.Delete(tempDbPath); } catch { /* best effort */ }
            }
        }
    }

    private void ApplySharedRestores(DecryptedArchive archive, ImportOptions options)
    {
        var payload = archive.Payload;
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
            RestoreSmtpPassword(archive, payload.Shared.SmtpPasswordEncrypted);
        }
    }

    private void RestoreSmtpPassword(DecryptedArchive archive, ExportedProtectedSecret secret)
    {
        var plaintextBytes = archive.DecryptSecret(secret);
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
}
