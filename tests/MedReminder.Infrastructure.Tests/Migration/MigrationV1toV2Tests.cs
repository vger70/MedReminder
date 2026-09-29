using System.Text.Json;
using FluentAssertions;
using MedReminder.Infrastructure.Migration;
using MedReminder.Infrastructure.Profiles;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Migration;

// Integration tests for the V1→V2 on-disk migration
// (docs/ANALYSIS-MULTI-USER.md §5.4). Each test builds a fake V1 tree
// under Path.GetTempPath(), runs MigrationV1toV2, and asserts the
// resulting V2 layout or (for the failure scenarios) the restored V1
// state.
public sealed class MigrationV1toV2Tests : IDisposable
{
    private readonly string _root;

    public MigrationV1toV2Tests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            $"medreminder-migration-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    private MigrationV1toV2 NewMigrator()
    {
        var registryPath = Path.Combine(_root, AppDataPaths.ProfilesRegistryFileName);
        var profilesRoot = Path.Combine(_root, "profiles");
        Directory.CreateDirectory(profilesRoot);
        var registry = new ProfileRegistry(registryPath, profilesRoot, TimeProvider.System);
        return new MigrationV1toV2(_root, registry, TimeProvider.System, NullLogger<MigrationV1toV2>.Instance);
    }

    [Fact]
    public void Fresh_install_without_legacy_db_reports_not_needed()
    {
        var sut = NewMigrator();

        var outcome = sut.Run();

        outcome.Should().Be(MigrationOutcome.NotNeeded);
        File.Exists(Path.Combine(_root, AppDataPaths.ProfilesRegistryFileName))
            .Should().BeFalse();
    }

    [Fact]
    public void Already_migrated_registry_reports_not_needed()
    {
        var registryPath = Path.Combine(_root, AppDataPaths.ProfilesRegistryFileName);
        File.WriteAllText(registryPath, "{ \"SchemaVersion\": 1, \"Profiles\": [] }");
        File.WriteAllText(Path.Combine(_root, "medreminder.db"), "leftover");

        var sut = NewMigrator();
        var outcome = sut.Run();

        outcome.Should().Be(MigrationOutcome.NotNeeded);
        // The pre-existing DB stays untouched — a live V2 installation
        // could not have this file, but the migrator refuses to
        // overwrite anything just in case.
        File.Exists(Path.Combine(_root, "medreminder.db")).Should().BeTrue();
    }

    [Fact]
    public void V1_tree_with_db_and_toaddress_produces_v2_layout()
    {
        SeedV1Tree(dbBytes: new byte[] { 0x53, 0x51, 0x4C }, walBytes: new byte[] { 1, 2, 3 });
        WriteV1SmtpSettings(toAddress: "family@example.org", host: "smtp.example.org");

        var sut = NewMigrator();
        var outcome = sut.Run();

        outcome.Should().Be(MigrationOutcome.Migrated);

        var defaultDir = Path.Combine(_root, "profiles", "default");
        File.Exists(Path.Combine(defaultDir, "medreminder.db")).Should().BeTrue();
        File.Exists(Path.Combine(defaultDir, "medreminder.db-wal")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "medreminder.db")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "medreminder.db-wal")).Should().BeFalse();

        var registryJson = File.ReadAllText(
            Path.Combine(_root, AppDataPaths.ProfilesRegistryFileName));
        using (var doc = JsonDocument.Parse(registryJson))
        {
            var root = doc.RootElement;
            root.GetProperty("ActiveProfileId").GetString().Should().Be("default");
            var profiles = root.GetProperty("Profiles");
            profiles.GetArrayLength().Should().Be(1);
            var p = profiles[0];
            p.GetProperty("Id").GetString().Should().Be("default");
            p.GetProperty("Role").GetString().Should().Be("admin");
            p.GetProperty("DisplayName").GetString().Should().Be("User");
        }

        // Per-profile notifications.settings.json now carries the
        // extracted ToAddress and the shared smtp.settings.json no
        // longer contains it.
        var notificationsPath = Path.Combine(defaultDir, "notifications.settings.json");
        File.Exists(notificationsPath).Should().BeTrue();
        using (var doc = JsonDocument.Parse(File.ReadAllText(notificationsPath)))
        {
            doc.RootElement.GetProperty("Notifications")
                .GetProperty("ToAddress").GetString().Should().Be("family@example.org");
        }

        var smtpAfter = File.ReadAllText(Path.Combine(_root, "smtp.settings.json"));
        using (var doc = JsonDocument.Parse(smtpAfter))
        {
            var smtp = doc.RootElement.GetProperty("Smtp");
            smtp.TryGetProperty("ToAddress", out _).Should().BeFalse();
            smtp.GetProperty("Host").GetString().Should().Be("smtp.example.org");
        }

        // Pre-migration backup preserved.
        var backupsRoot = Path.Combine(_root, "backups");
        Directory.Exists(backupsRoot).Should().BeTrue();
        var preBackupFolders = Directory.GetDirectories(backupsRoot, "pre-migration-*");
        preBackupFolders.Should().HaveCount(1);
        var backup = preBackupFolders[0];
        File.Exists(Path.Combine(backup, "medreminder.db")).Should().BeTrue();
        File.Exists(Path.Combine(backup, "medreminder.db-wal")).Should().BeTrue();
        File.Exists(Path.Combine(backup, "smtp.settings.json")).Should().BeTrue();
    }

    [Fact]
    public void V1_tree_without_smtp_file_still_migrates_db()
    {
        SeedV1Tree(dbBytes: new byte[] { 0x53, 0x51, 0x4C });

        var sut = NewMigrator();
        var outcome = sut.Run();

        outcome.Should().Be(MigrationOutcome.Migrated);
        File.Exists(Path.Combine(_root, "profiles", "default", "medreminder.db"))
            .Should().BeTrue();
        // No per-profile notifications file when there is no source
        // ToAddress to extract.
        File.Exists(Path.Combine(_root, "profiles", "default", "notifications.settings.json"))
            .Should().BeFalse();
    }

    [Fact]
    public void V1_tree_with_smtp_but_empty_toaddress_strips_key_but_creates_no_notifications_file()
    {
        SeedV1Tree(dbBytes: new byte[] { 0x53, 0x51, 0x4C });
        WriteV1SmtpSettings(toAddress: "", host: "smtp.example.org");

        var sut = NewMigrator();
        var outcome = sut.Run();

        outcome.Should().Be(MigrationOutcome.Migrated);
        File.Exists(Path.Combine(_root, "profiles", "default", "notifications.settings.json"))
            .Should().BeFalse();
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_root, "smtp.settings.json")));
        doc.RootElement.GetProperty("Smtp")
            .TryGetProperty("ToAddress", out _).Should().BeFalse();
    }

    [Fact]
    public void Rollback_restores_v1_state_when_seeding_fails()
    {
        // Simulate a mid-migration failure: leave a bogus
        // profiles.json in place before we run — the migrator's
        // idempotence guard bails out early, so that isn't the right
        // failure. Instead, make the target directory exist as a
        // regular FILE so Directory.CreateDirectory throws.
        SeedV1Tree(dbBytes: new byte[] { 0x53, 0x51, 0x4C });
        WriteV1SmtpSettings(toAddress: "user@example.org", host: "smtp.example.org");

        // profiles/default is meant to be a folder; making it a file
        // guarantees that File.Move fails on step 3 (the DB move).
        var profilesRoot = Path.Combine(_root, "profiles");
        Directory.CreateDirectory(profilesRoot);
        File.WriteAllText(Path.Combine(profilesRoot, "default"), "not a folder");

        var sut = NewMigrator();
        Action act = () => sut.Run();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*rolled back*");

        // V1 state restored: the DB is back at the root, smtp.settings.json
        // still carries ToAddress, no profiles.json was written.
        File.Exists(Path.Combine(_root, "medreminder.db")).Should().BeTrue();
        File.Exists(Path.Combine(_root, AppDataPaths.ProfilesRegistryFileName))
            .Should().BeFalse();
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_root, "smtp.settings.json")));
        doc.RootElement.GetProperty("Smtp")
            .GetProperty("ToAddress").GetString().Should().Be("user@example.org");

        // The pre-migration backup remains for manual inspection.
        Directory.GetDirectories(Path.Combine(_root, "backups"), "pre-migration-*")
            .Should().HaveCount(1);
    }

    // ---- fixture helpers ----

    private void SeedV1Tree(byte[] dbBytes, byte[]? walBytes = null, byte[]? shmBytes = null)
    {
        File.WriteAllBytes(Path.Combine(_root, "medreminder.db"), dbBytes);
        if (walBytes is not null)
        {
            File.WriteAllBytes(Path.Combine(_root, "medreminder.db-wal"), walBytes);
        }
        if (shmBytes is not null)
        {
            File.WriteAllBytes(Path.Combine(_root, "medreminder.db-shm"), shmBytes);
        }
    }

    private void WriteV1SmtpSettings(string toAddress, string host)
    {
        var json = $$"""
        {
          "Smtp": {
            "Host": "{{host}}",
            "Port": 587,
            "UseStartTls": true,
            "Username": "sender@example.org",
            "FromAddress": "sender@example.org",
            "FromDisplayName": "MedReminder",
            "ToAddress": "{{toAddress}}",
            "TimeoutSeconds": 30
          }
        }
        """;
        File.WriteAllText(Path.Combine(_root, "smtp.settings.json"), json);
    }
}
