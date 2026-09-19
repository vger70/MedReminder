using System.Text.Json;
using System.Text.Json.Nodes;
using MedReminder.Infrastructure.Profiles;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Migration;

// Data-lossless V1 → V2 on-disk migration
// (docs/ANALYSIS-MULTI-USER.md §5). Idempotent at the boot level: it
// only runs when profiles.json is missing AND a legacy medreminder.db
// exists at the app-data root. After a successful run the V1 layout
// is gone and the app boots as multi-user with a single "default"
// admin profile carrying the original database.
//
// The migrator is created dormant in 15b — Program.Main does not call
// it yet. Wiring lands in 15c together with the boot flow.
//
// Failure model (§5.2 step 7): any exception raised between steps
// 2 and 6 triggers a full rollback that puts the app-data folder
// back to its V1 state (DB back to the root, notifications file
// dropped, profiles.json dropped). The pre-migration backup at
// backups\pre-migration-YYYYMMDD-HHmmss\ is never touched again
// after step 1 — the user has to clean it up manually (§14 F).
public sealed class MigrationV1toV2
{
    private const string DatabaseFileName = "medreminder.db";
    private const string SmtpSettingsFileName = "smtp.settings.json";
    private const string NotificationSettingsFileName = "notifications.settings.json";
    private const string ProfilesFolderName = "profiles";
    private const string BackupsFolderName = "backups";
    private const string DefaultProfileId = "default";
    private const string DefaultProfileDisplayName = "User";

    private static readonly string[] DatabaseSideFileSuffixes = { "-wal", "-shm" };

    private readonly string _appDataRoot;
    private readonly ProfileRegistry _registry;
    private readonly TimeProvider _clock;
    private readonly ILogger<MigrationV1toV2> _log;

    public MigrationV1toV2(
        string appDataRoot,
        ProfileRegistry registry,
        TimeProvider clock,
        ILogger<MigrationV1toV2> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        _appDataRoot = appDataRoot;
        _registry = registry;
        _clock = clock;
        _log = log;
    }

    public MigrationOutcome Run()
    {
        var registryPath = Path.Combine(_appDataRoot, AppDataPaths.ProfilesRegistryFileName);
        var legacyDatabasePath = Path.Combine(_appDataRoot, DatabaseFileName);

        // Idempotence guard (§5.1): both preconditions must hold. If
        // profiles.json already exists we are already on V2, if the
        // legacy DB does not exist this is a fresh install — either
        // way there is nothing to migrate.
        if (File.Exists(registryPath))
        {
            _log.LogDebug("V1→V2 migration skipped: profiles.json already present.");
            return MigrationOutcome.NotNeeded;
        }
        if (!File.Exists(legacyDatabasePath))
        {
            _log.LogDebug("V1→V2 migration skipped: legacy database not found.");
            return MigrationOutcome.NotNeeded;
        }

        _log.LogInformation("V1→V2 migration starting from {Root}.", _appDataRoot);

        // Step 1: mandatory pre-migration backup. Distinct from the
        // regular automatic-backup folder — it is a self-describing,
        // never-overwritten snapshot of everything the migration
        // touches (DB + SMTP settings). Failure here aborts BEFORE
        // touching anything else.
        string preBackupDirectory;
        try
        {
            preBackupDirectory = CreatePreMigrationBackup(legacyDatabasePath);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "V1→V2 migration aborted before any file was moved: pre-backup failed.");
            throw;
        }

        // Steps 2-6 run inside a try/catch that rolls the folder back
        // on ANY exception. The rollback uses the pre-backup as the
        // source of truth for the legacy files.
        var profilesRoot = Path.Combine(_appDataRoot, ProfilesFolderName);
        var defaultDirectory = Path.Combine(profilesRoot, DefaultProfileId);
        var smtpSettingsPath = Path.Combine(_appDataRoot, SmtpSettingsFileName);
        var notificationSettingsPath = Path.Combine(defaultDirectory, NotificationSettingsFileName);

        try
        {
            // Step 2-3: DB move.
            Directory.CreateDirectory(defaultDirectory);
            MoveDatabaseFiles(legacyDatabasePath, defaultDirectory);

            // Step 4: SMTP settings are already at app-data root in
            // V1 and stay there in V2 — nothing to move (§5.2 step 4).

            // Step 5: extract ToAddress from smtp.settings.json into
            // the new per-profile notifications.settings.json,
            // rewriting the source file with the ToAddress key
            // removed. Absent or empty source → no-op.
            ExtractToAddress(smtpSettingsPath, notificationSettingsPath);

            // Step 6: write profiles.json via the registry so
            // formatting stays owned by a single class.
            _registry.SeedFromV1Migration(
                DefaultProfileId, DefaultProfileDisplayName);

            _log.LogInformation(
                "V1→V2 migration complete. Legacy DB moved to {Directory}; pre-backup preserved at {Backup}.",
                defaultDirectory, preBackupDirectory);
            return MigrationOutcome.Migrated;
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "V1→V2 migration failed after step 1. Rolling back to pre-migration state.");
            try
            {
                RollbackFromPreBackup(
                    preBackupDirectory,
                    legacyDatabasePath,
                    defaultDirectory,
                    profilesRoot,
                    smtpSettingsPath,
                    registryPath);
            }
            catch (Exception rollbackEx)
            {
                _log.LogCritical(rollbackEx,
                    "V1→V2 rollback partially failed. Pre-migration backup at {Backup} remains available for manual recovery.",
                    preBackupDirectory);
            }
            throw new InvalidOperationException(
                $"V1→V2 migration rolled back. Pre-migration backup preserved at '{preBackupDirectory}'.",
                ex);
        }
    }

    // ---- Step implementations ----

    private string CreatePreMigrationBackup(string legacyDatabasePath)
    {
        var timestamp = _clock.GetUtcNow().ToString("yyyyMMdd-HHmmss");
        var backupsRoot = Path.Combine(_appDataRoot, BackupsFolderName);
        var directory = Path.Combine(backupsRoot, $"pre-migration-{timestamp}");
        if (Directory.Exists(directory))
        {
            // Extremely unlikely (same-second call) but the design
            // says "never overwritten", so append a suffix instead.
            directory += "-" + Guid.NewGuid().ToString("N")[..6];
        }
        Directory.CreateDirectory(directory);

        // Copy the DB and its side files (may not exist if the app
        // was cleanly shut down and SQLite committed the WAL).
        File.Copy(legacyDatabasePath, Path.Combine(directory, DatabaseFileName), overwrite: false);
        foreach (var suffix in DatabaseSideFileSuffixes)
        {
            var side = legacyDatabasePath + suffix;
            if (File.Exists(side))
            {
                File.Copy(side, Path.Combine(directory, DatabaseFileName + suffix), overwrite: false);
            }
        }

        // Copy smtp.settings.json if present — rollback restores it
        // untouched (step 5 rewrites it in place).
        var smtpSettingsPath = Path.Combine(_appDataRoot, SmtpSettingsFileName);
        if (File.Exists(smtpSettingsPath))
        {
            File.Copy(smtpSettingsPath, Path.Combine(directory, SmtpSettingsFileName), overwrite: false);
        }

        return directory;
    }

    private static void MoveDatabaseFiles(string legacyDatabasePath, string defaultDirectory)
    {
        File.Move(legacyDatabasePath, Path.Combine(defaultDirectory, DatabaseFileName));
        foreach (var suffix in DatabaseSideFileSuffixes)
        {
            var side = legacyDatabasePath + suffix;
            if (File.Exists(side))
            {
                File.Move(side, Path.Combine(defaultDirectory, DatabaseFileName + suffix));
            }
        }
    }

    private static void ExtractToAddress(string smtpSettingsPath, string notificationSettingsPath)
    {
        if (!File.Exists(smtpSettingsPath))
        {
            return;
        }

        JsonNode? root;
        try
        {
            using var stream = File.OpenRead(smtpSettingsPath);
            root = JsonNode.Parse(stream, new JsonNodeOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            // A corrupt smtp.settings.json is a V1 problem we do not
            // fix here — leave it alone. The extraction is a
            // best-effort step 5; the migration itself does not fail.
            return;
        }

        if (root is not JsonObject rootObject) return;
        if (!TryGetChildIgnoreCase(rootObject, "Smtp", out var smtpNode) ||
            smtpNode is not JsonObject smtpObject)
        {
            return;
        }
        if (!TryGetChildIgnoreCase(smtpObject, "ToAddress", out var toNode) ||
            toNode is null)
        {
            return;
        }

        var toAddress = toNode.GetValue<string?>();
        if (string.IsNullOrWhiteSpace(toAddress))
        {
            // Empty ToAddress: strip the key from smtp.settings.json
            // (schema is moving anyway in 15c) but do not create the
            // per-profile file — an empty recipient is not worth
            // materialising.
            RemoveChildIgnoreCase(smtpObject, "ToAddress");
            RewriteJson(smtpSettingsPath, root);
            return;
        }

        // Write the per-profile notifications file first: if this
        // fails, the source smtp.settings.json still carries
        // ToAddress and the rollback can restore either way.
        var notification = new JsonObject
        {
            ["Notifications"] = new JsonObject
            {
                ["ToAddress"] = toAddress,
            },
        };
        Directory.CreateDirectory(Path.GetDirectoryName(notificationSettingsPath)!);
        RewriteJson(notificationSettingsPath, notification);

        // Now strip ToAddress from the source.
        RemoveChildIgnoreCase(smtpObject, "ToAddress");
        RewriteJson(smtpSettingsPath, root);
    }

    // ---- Rollback ----

    private static void RollbackFromPreBackup(
        string preBackupDirectory,
        string legacyDatabasePath,
        string defaultDirectory,
        string profilesRoot,
        string smtpSettingsPath,
        string registryPath)
    {
        // profiles.json: delete if it was written (step 6 succeeded
        // but a later step — none exists — failed) or half-written.
        if (File.Exists(registryPath))
        {
            try { File.Delete(registryPath); } catch { /* best-effort */ }
        }

        // profiles/default: nuke the whole folder. Its DB has
        // already been rolled back by the copy below, and its
        // notifications.settings.json is regenerated on next
        // migration attempt.
        if (Directory.Exists(defaultDirectory))
        {
            try { Directory.Delete(defaultDirectory, recursive: true); } catch { /* best-effort */ }
        }
        // Remove the profiles/ folder if it is now empty — leaves
        // the app-data root indistinguishable from a fresh V1 state.
        if (Directory.Exists(profilesRoot))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(profilesRoot).Any())
                {
                    Directory.Delete(profilesRoot);
                }
            }
            catch { /* best-effort */ }
        }

        // Restore the DB and side files. File.Move above may have
        // succeeded (source gone, target present) or failed halfway.
        // The pre-backup is the source of truth.
        var backupDb = Path.Combine(preBackupDirectory, DatabaseFileName);
        if (File.Exists(backupDb) && !File.Exists(legacyDatabasePath))
        {
            File.Copy(backupDb, legacyDatabasePath, overwrite: false);
        }
        foreach (var suffix in DatabaseSideFileSuffixes)
        {
            var backupSide = Path.Combine(preBackupDirectory, DatabaseFileName + suffix);
            var liveSide = legacyDatabasePath + suffix;
            if (File.Exists(backupSide) && !File.Exists(liveSide))
            {
                File.Copy(backupSide, liveSide, overwrite: false);
            }
        }

        // Restore smtp.settings.json from the pre-backup — this
        // undoes the ToAddress removal from step 5 whether or not
        // the per-profile file was written.
        var backupSmtp = Path.Combine(preBackupDirectory, SmtpSettingsFileName);
        if (File.Exists(backupSmtp))
        {
            File.Copy(backupSmtp, smtpSettingsPath, overwrite: true);
        }
    }

    // ---- JsonNode helpers ----

    private static bool TryGetChildIgnoreCase(JsonObject parent, string key, out JsonNode? child)
    {
        foreach (var kvp in parent)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                child = kvp.Value;
                return true;
            }
        }
        child = null;
        return false;
    }

    private static void RemoveChildIgnoreCase(JsonObject parent, string key)
    {
        string? actualKey = null;
        foreach (var kvp in parent)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                actualKey = kvp.Key;
                break;
            }
        }
        if (actualKey is not null)
        {
            parent.Remove(actualKey);
        }
    }

    private static void RewriteJson(string path, JsonNode content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = content.ToJsonString(options);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}

public enum MigrationOutcome
{
    NotNeeded,
    Migrated,
}
