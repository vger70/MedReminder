using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using MedReminder.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Export;

// The profile whose database an archive is written from or into.
// NotificationSettingsPath is the per-profile notifications.settings.json,
// which travels with the profile (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md
// §3.2).
public sealed record ProfileArchiveProfile(
    string Id,
    string DisplayName,
    ProfileRole Role,
    string DatabasePath,
    string NotificationSettingsPath);

// The encrypted .mrz export and import of one profile for a host that
// owns a single profile database and works on streams: the Android app
// (docs/analysis/ANALYSIS-B1-ANDROID-IMPLEMENTATION-BACKLOG.md B1-06),
// whose documents come from the system file picker. Same format, same
// payload and same database build as the Windows ExportService and
// ImportService, which share ArchiveWriter, ProfilePayloadReader,
// ArchiveReader, ProfileDatabaseBuilder and ProfileDatabaseSwap with it.
// Profile scope only; the opt-in shared settings (SMTP, backup, user
// interface) are neither written nor restored.
//
// Security (CLAUDE.md §7): the passphrase, the derived key and the
// payload plaintext are never logged. The caller owns the passphrase
// buffer and zeroes it after the call.
public sealed class ProfileArchive
{
    private readonly IArchiveCipher _cipher;
    private readonly IArchiveReader _reader;
    private readonly IDatabaseExclusiveAccess _exclusiveAccess;
    private readonly TimeProvider _clock;

    public ProfileArchive(
        IArchiveCipher cipher, IArchiveReader reader, IDatabaseExclusiveAccess exclusiveAccess, TimeProvider clock)
    {
        _cipher = cipher;
        _reader = reader;
        _exclusiveAccess = exclusiveAccess;
        _clock = clock;
    }

    // Writes the profile to `destination` (§4.1): a snapshot of the live
    // database through the SQLite online-backup API in `scratchDirectory`,
    // read into the payload, encrypted and zipped. The snapshot is
    // deleted afterwards. A passphrase shorter than
    // ExportFormat.MinPassphraseLength is rejected before anything is
    // read or written. automaticDeviceName marks a scheduled cloud
    // snapshot (C.3+ §3.6): source "automatic" and the hashed device
    // name in the manifest; null for an export the user started.
    public async Task ExportAsync(
        ProfileArchiveProfile profile,
        string appVersion,
        Stream destination,
        char[] passphrase,
        string scratchDirectory,
        CancellationToken cancellationToken,
        string? automaticDeviceName = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);
        ArchiveWriter.ValidatePassphrase(passphrase);

        var scratch = Path.Combine(scratchDirectory, "export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var snapshot = Path.Combine(scratch, "snapshot.db");
            await SnapshotAsync(profile.DatabasePath, snapshot, cancellationToken);

            ExportPayload payload;
            var options = new DbContextOptionsBuilder<MedReminderDbContext>()
                .UseSqlite(SqliteConnectionStrings.ForFile(snapshot))
                .Options;
            await using (var db = new MedReminderDbContext(options))
            {
                var info = new ExportedProfileInfo
                {
                    Id = profile.Id,
                    DisplayName = profile.DisplayName,
                    Role = profile.Role.ToString(),
                    CreatedAt = _clock.GetUtcNow(),
                };
                payload = await ProfilePayloadReader.ReadAsync(db, info, cancellationToken);
            }

            payload.NotificationSettings =
                ExportSettingsFiles.ReadNotificationSettings(profile.NotificationSettingsPath);

            var content = new ArchiveContent
            {
                Payload = payload,
                ProfileId = profile.Id,
                AppVersion = appVersion,
                CreatedAtUtc = _clock.GetUtcNow(),
                Source = automaticDeviceName is null ? null : AutomaticArchiveSource.Source,
                Device = automaticDeviceName is null ? null : AutomaticArchiveSource.Device(automaticDeviceName, profile.Id),
            };
            await Task.Run(() => ArchiveWriter.Write(destination, content, passphrase, _cipher), cancellationToken);
        }
        finally
        {
            // The snapshot connections stay pooled until cleared, and an
            // open file cannot be deleted on every platform.
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // The clear-text manifest: format, creation time and app version,
    // readable without the passphrase (§4.2 steps 1-2).
    public ExportManifest ReadManifest(Stream archive) => _reader.ReadManifest(archive);

    // Decrypts and validates the archive without applying it, so the
    // host can show what it holds (profile name, medicines) before the
    // user confirms the replacement. The caller disposes the result.
    public DecryptedArchive Decrypt(Stream archive, char[] passphrase) => _reader.Decrypt(archive, passphrase);

    // Replaces the profile's database with the archive's content
    // (§4.2 steps 8-10): the new database is built in a temporary file
    // next to the current one, which is kept as <db>.bak-<timestamp>,
    // and moved into place under IDatabaseExclusiveAccess, so no write
    // is in flight. A failure before the swap leaves the current
    // database untouched. On a synced profile the import starts a new
    // sync generation (B.1 Phase 3d). The host reopens the database
    // afterwards (boot patches, scheduled work).
    public async Task ReplaceAsync(
        DecryptedArchive archive, ProfileArchiveProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(profile);

        var directory = Path.GetDirectoryName(Path.GetFullPath(profile.DatabasePath))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $"medreminder.import-{Guid.NewGuid():N}.db");
        try
        {
            await ProfileDatabaseBuilder.BuildAsync(temp, archive.Payload, cancellationToken, _clock);
            SqliteConnection.ClearAllPools();
            await ProfileDatabaseSwap.ReplaceAsync(
                _exclusiveAccess, db: null, profile.DatabasePath, temp, _clock, cancellationToken,
                beforeSwap: () => JsonSyncSettingsStore.MarkResetPending(directory));
        }
        finally
        {
            if (File.Exists(temp))
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        if (archive.Payload.NotificationSettings is { } settings)
        {
            ExportSettingsFiles.WriteNotificationSettings(profile.NotificationSettingsPath, settings);
        }
    }

    private static async Task SnapshotAsync(string source, string destination, CancellationToken cancellationToken)
    {
        if (!File.Exists(source))
        {
            throw new InvalidOperationException("The profile database does not exist.");
        }

        var sourceConnection = new SqliteConnectionStringBuilder(SqliteConnectionStrings.ForFile(source))
        {
            Cache = SqliteCacheMode.Private,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
        var destinationConnection = new SqliteConnectionStringBuilder
        {
            DataSource = destination,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        await using var from = new SqliteConnection(sourceConnection);
        await using var to = new SqliteConnection(destinationConnection);
        await from.OpenAsync(cancellationToken);
        await to.OpenAsync(cancellationToken);
        await Task.Run(() => from.BackupDatabase(to), cancellationToken);
    }
}
