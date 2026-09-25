using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Backup;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Export;

// Export-side coverage for C.3 Step 4
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.1). The full round-trip
// through ImportService is exercised in Step 6; here the produced
// archive is validated directly (ZIP layout, manifest fields) and the
// payload is decrypted with the archive cipher to prove the export
// content and encryption are correct without depending on the importer.
//
// A fresh GUID profile is created under
// %LOCALAPPDATA%\MedReminder\profiles\ so the real per-profile paths
// used by BackupService resolve; it is removed on Dispose.
[SupportedOSPlatform("windows")]
public sealed class ExportServiceTests : IDisposable
{
    private readonly string _profileId;
    private readonly string _otherProfileId = Guid.NewGuid().ToString("N");
    private readonly FakeCurrentProfile _profile;
    private readonly string _sharedDirectory;
    private readonly IArchiveCipher _cipher = new ArchiveCipher();

    private const string Passphrase = "correct horse battery staple";

    public ExportServiceTests()
    {
        _profileId = Guid.NewGuid().ToString("N");
        _profile = new FakeCurrentProfile(_profileId);
        _sharedDirectory = Path.Combine(
            Path.GetTempPath(), "mr-export-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sharedDirectory);
    }

    [Fact]
    public async Task Export_writes_a_zip_with_manifest_and_payload_entries()
    {
        await SeedAsync();
        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            var progress = new ProgressCollector();
            var result = await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), progress, CancellationToken.None);

            result.Should().Be(archivePath);
            progress.Values.Should().Contain(0).And.Contain(50).And.Contain(100);

            using var archive = ZipFile.OpenRead(archivePath);
            archive.GetEntry(ExportFormat.ManifestEntryName).Should().NotBeNull();
            archive.GetEntry(ExportFormat.PayloadEntryName).Should().NotBeNull();
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Manifest_declares_the_format_kdf_and_cipher_parameters()
    {
        await SeedAsync();
        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            var manifest = ReadManifest(archivePath);

            manifest.Format.Should().Be(ExportFormat.FormatIdentifier);
            manifest.FormatVersion.Should().Be(ExportFormat.CurrentFormatVersion);
            manifest.Scope.Should().Be("profile");
            manifest.ProfileId.Should().Be(_profileId);
            manifest.Kdf.Algorithm.Should().Be("Argon2id");
            manifest.Kdf.Iterations.Should().Be(Argon2Params.Default.Iterations);
            manifest.Kdf.MemoryKiB.Should().Be(Argon2Params.Default.MemoryKiB);
            manifest.Kdf.Parallelism.Should().Be(Argon2Params.Default.Parallelism);
            manifest.Kdf.SaltBase64.Should().NotBeNullOrEmpty();
            manifest.Cipher.Algorithm.Should().Be("AES-GCM");
            manifest.Cipher.KeyBits.Should().Be(256);
            manifest.Cipher.NonceBase64.Should().NotBeNullOrEmpty();
            manifest.Cipher.TagBase64.Should().NotBeNullOrEmpty();
            manifest.Payload.Sha256Base64.Should().NotBeNullOrEmpty();
            manifest.Payload.SizeBytes.Should().BeGreaterThan(0);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Payload_decrypts_with_the_manifest_parameters_and_matches_the_hash()
    {
        await SeedAsync();
        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            var manifest = ReadManifest(archivePath);
            var payloadBytes = DecryptPayload(archivePath, manifest);

            // The hash in the manifest is over the decrypted plaintext.
            var hash = Convert.ToBase64String(SHA256.HashData(payloadBytes));
            hash.Should().Be(manifest.Payload.Sha256Base64);
            payloadBytes.Length.Should().Be((int)manifest.Payload.SizeBytes);

            var payload = JsonSerializer.Deserialize<ExportPayload>(
                payloadBytes, ExportJson.Options)!;
            payload.SchemaVersion.Should().Be(ExportFormat.CurrentSchemaVersion);
            payload.Medicines.Should().HaveCount(1);
            payload.Medicines[0].Name.Should().Be("Metformin");
            payload.StockMovements.Should().HaveCount(1);
            payload.MedicationScheduleHistory.Should().HaveCount(1);
            payload.MedicationAdministrationSlots.Should().HaveCount(1);
            payload.MedicationSuspensions.Should().HaveCount(1);
            payload.MedicationIntakes.Should().HaveCount(1);
            payload.NotificationEvents.Should().HaveCount(1);
            payload.DoseReminderEvents.Should().HaveCount(1);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Export_refuses_a_short_passphrase_before_writing_any_file()
    {
        await SeedAsync();
        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        var shortPassphrase = new string('a', ExportFormat.MinPassphraseLength - 1);

        var act = () => export.ExportAsync(
            new ExportOptions { DestinationPath = archivePath },
            shortPassphrase.ToCharArray(), null, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ExportValidationException>();
        ex.Which.Reason.Should().Be(ExportValidationReason.PassphraseTooShort);
        File.Exists(archivePath).Should().BeFalse();
    }

    [Fact]
    public async Task Opt_out_leaves_shared_sections_and_includes_flags_empty()
    {
        await SeedAsync();
        WriteSmtpSettingsFile();

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore { Password = "pw" });
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            var manifest = ReadManifest(archivePath);
            manifest.Includes.SmtpSettings.Should().BeFalse();
            manifest.Includes.SmtpCredential.Should().BeFalse();

            var payload = JsonSerializer.Deserialize<ExportPayload>(
                DecryptPayload(archivePath, manifest), ExportJson.Options)!;
            payload.Shared.SmtpSettings.Should().BeNull();
            payload.Shared.SmtpPasswordEncrypted.Should().BeNull();
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Opt_in_carries_smtp_settings_and_reencrypts_the_password()
    {
        await SeedAsync();
        WriteSmtpSettingsFile();

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore { Password = "s3cr3t" });
        try
        {
            await export.ExportAsync(
                new ExportOptions
                {
                    DestinationPath = archivePath,
                    IncludeSmtpSettings = true,
                    IncludeSmtpPassword = true,
                },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            var manifest = ReadManifest(archivePath);
            manifest.Includes.SmtpSettings.Should().BeTrue();
            manifest.Includes.SmtpCredential.Should().BeTrue();

            var payload = JsonSerializer.Deserialize<ExportPayload>(
                DecryptPayload(archivePath, manifest), ExportJson.Options)!;
            payload.Shared.SmtpSettings.Should().NotBeNull();
            payload.Shared.SmtpSettings!.Host.Should().Be("smtp.example.com");

            // The password is present but never in the clear: decrypt it
            // with the archive key derived from the manifest.
            var secret = payload.Shared.SmtpPasswordEncrypted;
            secret.Should().NotBeNull();
            var key = DeriveKey(manifest);
            try
            {
                var plaintext = _cipher.Decrypt(
                    key,
                    Convert.FromBase64String(secret!.NonceBase64),
                    Convert.FromBase64String(secret.TagBase64),
                    Convert.FromBase64String(secret.CiphertextBase64));
                System.Text.Encoding.UTF8.GetString(plaintext).Should().Be("s3cr3t");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Export_deletes_its_temporary_snapshot()
    {
        await SeedAsync();
        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());

        // Snapshot pre-existing scratch dirs (from other concurrent
        // tests / prior runs) so the assertion only sees what THIS
        // export leaves behind.
        var before = Directory
            .EnumerateDirectories(Path.GetTempPath(), "MedReminder-export-*")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            // This export's own temp snapshot directory must be gone.
            var after = Directory
                .EnumerateDirectories(Path.GetTempPath(), "MedReminder-export-*")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            after.Except(before).Should().BeEmpty();
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Export_of_another_profile_carries_that_profiles_data_and_id()
    {
        await SeedAsync();
        await SeedAsync(OtherProfileDatabasePath, medicineName: "Warfarin");
        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore(), profileRegistry: TwoProfileRegistry());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath, ProfileId = _otherProfileId },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            var manifest = ReadManifest(archivePath);
            manifest.Scope.Should().Be("profile");
            manifest.ProfileId.Should().Be(_otherProfileId);

            var payload = JsonSerializer.Deserialize<ExportPayload>(
                DecryptPayload(archivePath, manifest), ExportJson.Options)!;
            payload.Profile.Id.Should().Be(_otherProfileId);
            payload.Profile.DisplayName.Should().Be("Other");
            payload.Medicines.Should().ContainSingle().Which.Name.Should().Be("Warfarin");
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Non_admin_cannot_export_another_profile()
    {
        await SeedAsync(OtherProfileDatabasePath, medicineName: "Warfarin");
        var archivePath = NewArchivePath();
        var export = CreateExportService(
            new FakeCredentialStore(),
            new FakeCurrentProfile(_profileId, isAdmin: false),
            TwoProfileRegistry());

        var act = () => export.ExportAsync(
            new ExportOptions { DestinationPath = archivePath, ProfileId = _otherProfileId },
            Passphrase.ToCharArray(), null, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ExportValidationException>();
        ex.Which.Reason.Should().Be(ExportValidationReason.ScopeNotPermitted);
        File.Exists(archivePath).Should().BeFalse();
    }

    [Fact]
    public async Task Automatic_backup_may_export_another_profile_from_a_non_admin_profile()
    {
        await SeedAsync(OtherProfileDatabasePath, medicineName: "Warfarin");
        var archivePath = NewArchivePath();
        var export = CreateExportService(
            new FakeCredentialStore(),
            new FakeCurrentProfile(_profileId, isAdmin: false),
            TwoProfileRegistry());
        try
        {
            await export.ExportAsync(
                new ExportOptions
                {
                    DestinationPath = archivePath,
                    ProfileId = _otherProfileId,
                    AutomaticSource = true,
                },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            var manifest = ReadManifest(archivePath);
            manifest.ProfileId.Should().Be(_otherProfileId);
            manifest.Device!.ProfileId.Should().Be(_otherProfileId);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Export_of_an_unknown_profile_is_rejected()
    {
        await SeedAsync();
        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore(), profileRegistry: TwoProfileRegistry());

        var act = () => export.ExportAsync(
            new ExportOptions
            {
                DestinationPath = archivePath,
                ProfileId = Guid.NewGuid().ToString("N"),
            },
            Passphrase.ToCharArray(), null, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ExportValidationException>();
        ex.Which.Reason.Should().Be(ExportValidationReason.ProfileNotFound);
        File.Exists(archivePath).Should().BeFalse();
    }

    // -------- helpers --------

    private static string NewArchivePath()
        => Path.Combine(
            Path.GetTempPath(),
            "mr-export-" + Guid.NewGuid().ToString("N") + ExportFormat.ArchiveExtension);

    private ExportService CreateExportService(
        ISmtpCredentialStore credentialStore,
        ICurrentProfile? currentProfile = null,
        IProfileRegistry? profileRegistry = null)
    {
        var backupContext = CreateProfileContext();
        var backupService = new BackupService(
            backupContext, TimeProvider.System, new DatabasePathProvider(_profile.DatabasePath));
        return new ExportService(
            currentProfile ?? _profile, backupService, _cipher, credentialStore, TimeProvider.System,
            NullLogger<ExportService>.Instance, _sharedDirectory, profileRegistry);
    }

    private IProfileRegistry TwoProfileRegistry() => new FakeProfileRegistry(
        new Profile(_profileId, "Test Profile", ProfileRole.Admin,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, HasPin: false),
        new Profile(_otherProfileId, "Other", ProfileRole.User,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, HasPin: true));

    private string OtherProfileDatabasePath => Path.Combine(
        AppDataPaths.GetProfileDataDirectory(_otherProfileId), AppDataPaths.DatabaseFileName);

    private MedReminderDbContext CreateProfileContext(string? databasePath = null)
    {
        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(AppDataPaths.BuildSqliteConnectionString(databasePath ?? _profile.DatabasePath))
            .Options;
        return new MedReminderDbContext(options);
    }

    private async Task SeedAsync(string? databasePath = null, string medicineName = "Metformin")
    {
        await using var db = CreateProfileContext(databasePath);
        await db.Database.EnsureCreatedAsync();

        var medicineId = Guid.NewGuid();
        db.Medicines.Add(new Medicine
        {
            Id = medicineId,
            Name = medicineName,
            ActiveIngredient = "metformin hydrochloride",
            Unit = "tablets",
            DosePerAdministration = 1.5m,
            AdministrationsPerDay = 2,
            StartDate = new DateOnly(2026, 1, 10),
            ThresholdDays = 7,
            IsActive = true,
            StockEpoch = 3,
            NotificationChannels = NotificationChannels.Both,
            CreatedAt = new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 2, 1, 9, 30, 0, TimeSpan.Zero),
            RemindOnDose = true,
            AtcCode = MedReminder.Domain.Catalogue.AtcCode.Parse("A10BA02"),
        });
        db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            OccurredAt = new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero),
            Kind = StockMovementKind.InitialLoad,
            QuantityDelta = 60m,
            StockEpoch = 1,
        });
        db.MedicationScheduleHistories.Add(new MedicationScheduleHistory
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            EffectiveFrom = new DateOnly(2026, 1, 10),
            DosePerAdministration = 1.5m,
            AdministrationsPerDay = 2,
            ScheduleKind = ScheduleKind.Weekly,
            SchedulePayload = "{\"days\":[1,3,5]}",
        });
        db.MedicationAdministrationSlots.Add(new MedicationAdministrationSlot
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            Dose = 0.75m,
            Time = new TimeOnly(8, 30),
            TimingLabel = "after breakfast",
            Order = 0,
        });
        db.MedicationSuspensions.Add(new MedicationSuspension
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            StartDate = new DateOnly(2026, 4, 1),
            Reason = "surgery",
        });
        db.MedicationIntakes.Add(new MedicationIntake
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            Day = new DateOnly(2026, 1, 11),
            Quantity = 1.5m,
            Status = IntakeStatus.Taken,
        });
        db.NotificationEvents.Add(new NotificationEvent
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            StockEpoch = 3,
            TriggeredAt = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero),
            Channel = NotificationChannels.Email,
            DaysRemainingAtSend = 5,
            Success = true,
        });
        db.DoseReminderEvents.Add(new DoseReminderEvent
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            SlotKey = "08:30",
            LocalDate = new DateOnly(2026, 1, 11),
            FiredAt = new DateTimeOffset(2026, 1, 11, 8, 30, 0, TimeSpan.Zero),
            Channel = NotificationChannels.Windows,
        });

        await db.SaveChangesAsync();
    }

    private void WriteSmtpSettingsFile()
    {
        var payload = new
        {
            Smtp = new
            {
                Host = "smtp.example.com",
                Port = 587,
                UseStartTls = true,
                Username = "user@example.com",
                FromAddress = "from@example.com",
                FromDisplayName = "MedReminder",
                TimeoutSeconds = 30,
            },
        };
        File.WriteAllText(
            Path.Combine(_sharedDirectory, "smtp.settings.json"),
            JsonSerializer.Serialize(payload));
    }

    private static ExportManifest ReadManifest(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        using var stream = archive.GetEntry(ExportFormat.ManifestEntryName)!.Open();
        return JsonSerializer.Deserialize<ExportManifest>(stream, ExportJson.Options)!;
    }

    private byte[] DecryptPayload(string archivePath, ExportManifest manifest)
    {
        byte[] ciphertext;
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            using var stream = archive.GetEntry(ExportFormat.PayloadEntryName)!.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            ciphertext = buffer.ToArray();
        }

        var key = DeriveKey(manifest);
        try
        {
            return _cipher.Decrypt(
                key,
                Convert.FromBase64String(manifest.Cipher.NonceBase64),
                Convert.FromBase64String(manifest.Cipher.TagBase64),
                ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] DeriveKey(ExportManifest manifest)
        => _cipher.DeriveKey(
            Passphrase.ToCharArray(),
            Convert.FromBase64String(manifest.Kdf.SaltBase64),
            new Argon2Params
            {
                Iterations = manifest.Kdf.Iterations,
                MemoryKiB = manifest.Kdf.MemoryKiB,
                Parallelism = manifest.Kdf.Parallelism,
            });

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(AppDataPaths.GetProfileDataDirectory(_profileId), recursive: true);
        }
        catch { /* best effort */ }
        try
        {
            Directory.Delete(AppDataPaths.GetProfileDataDirectory(_otherProfileId), recursive: true);
        }
        catch { /* best effort */ }
        try { Directory.Delete(_sharedDirectory, recursive: true); } catch { /* best effort */ }
    }

    private sealed class ProgressCollector : IProgress<int>
    {
        public List<int> Values { get; } = new();
        public void Report(int value) => Values.Add(value);
    }

    private sealed class FakeCredentialStore : ISmtpCredentialStore
    {
        public string? Password { get; set; }
        public bool HasPassword => !string.IsNullOrEmpty(Password);
        public string? GetPassword() => Password;
        public void SetPassword(string password) => Password = password;
        public void Clear() => Password = null;
    }

    private sealed class FakeProfileRegistry(params Profile[] profiles) : IProfileRegistry
    {
        public IReadOnlyList<Profile> ListProfiles() => profiles;
        public Profile? GetById(string id) => profiles.SingleOrDefault(p => p.Id == id);
        public string? ActiveProfileIdHint => null;
        public Profile Create(string displayName, ProfileRole role) => throw new NotSupportedException();
        public void Rename(string id, string newDisplayName) => throw new NotSupportedException();
        public void Delete(string id, bool deleteData) => throw new NotSupportedException();
        public void SetActiveProfileHint(string id) => throw new NotSupportedException();
        public void SetPin(string id, string? pin) => throw new NotSupportedException();
        public bool VerifyPin(string id, string pin) => throw new NotSupportedException();
        public bool HasPin(string id) => false;
    }

    private sealed class FakeCurrentProfile : ICurrentProfile
    {
        public FakeCurrentProfile(string id, bool isAdmin = true)
        {
            IsAdmin = isAdmin;
            Id = id;
            DataDirectory = AppDataPaths.GetProfileDataDirectory(id);
            DatabasePath = Path.Combine(DataDirectory, AppDataPaths.DatabaseFileName);
            NotificationSettingsPath = Path.Combine(DataDirectory, "notifications.settings.json");
        }

        public string Id { get; }
        public string DisplayName => "Test Profile";
        public ProfileRole Role => IsAdmin ? ProfileRole.Admin : ProfileRole.User;
        public bool IsAdmin { get; }
        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string NotificationSettingsPath { get; }
    }
}
