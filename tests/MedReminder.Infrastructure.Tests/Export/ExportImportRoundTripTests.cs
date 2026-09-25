using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

// C.3 Step 6 — end-to-end round-trip and failure-mode coverage
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §8). Exercises the real
// ExportService and ImportService against a real per-profile SQLite DB
// created under %LOCALAPPDATA%\MedReminder\profiles\<guid>\ and removed
// on Dispose.
//
// Windows-only: the import path DPAPI-re-encrypts the SMTP password and
// relies on SqliteConnection.ClearAllPools to release the live DB file
// before the swap, both Windows behaviours.
//
// The Argon2id KDF parameter round-trip and the AES-GCM tamper
// rejection at the primitive level are covered by ArchiveCipherTests;
// the manifest-only failure surfaces (missing entry, non-zip, missing
// file) by ImportServiceManifestTests. This file covers the full
// export -> wipe -> import path and the payload-level failure surfaces.
[SupportedOSPlatform("windows")]
public sealed class ExportImportRoundTripTests : IDisposable
{
    private readonly string _profileId;
    private readonly FakeCurrentProfile _profile;
    private readonly string _sharedDirectory;
    private readonly IArchiveCipher _cipher = new ArchiveCipher();

    private const string Passphrase = "correct horse battery staple";

    public ExportImportRoundTripTests()
    {
        _profileId = Guid.NewGuid().ToString("N");
        _profile = new FakeCurrentProfile(_profileId);
        _sharedDirectory = Path.Combine(
            Path.GetTempPath(), "mr-e2e-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sharedDirectory);
    }

    [Fact]
    public async Task Export_then_import_restores_every_entity()
    {
        await SeedAsync();

        CapturedCounts before;
        await using (var db = CreateProfileContext())
        {
            before = await CapturedCounts.FromAsync(db);
        }

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            await WipeProfileDatabaseAsync();

            await using (var live = CreateProfileContext())
            {
                var import = CreateImportService(live, new FakeCredentialProtector());
                await import.ImportAsync(
                    archivePath, Passphrase.ToCharArray(),
                    new ImportOptions(), null, CancellationToken.None);
            }

            await using var reopened = CreateProfileContext();
            var after = await CapturedCounts.FromAsync(reopened);
            after.Should().BeEquivalentTo(before);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Import_restores_a_rich_medicine_field_for_field()
    {
        await SeedAsync();

        Medicine originalRich;
        Medicine originalBare;
        await using (var db = CreateProfileContext())
        {
            originalRich = await db.Medicines.AsNoTracking().SingleAsync(m => m.Name == "Metformin");
            originalBare = await db.Medicines.AsNoTracking().SingleAsync(m => m.Name == "Aspirin");
        }

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            await WipeProfileDatabaseAsync();

            await using (var live = CreateProfileContext())
            {
                var import = CreateImportService(live, new FakeCredentialProtector());
                await import.ImportAsync(
                    archivePath, Passphrase.ToCharArray(),
                    new ImportOptions(), null, CancellationToken.None);
            }

            await using var reopened = CreateProfileContext();
            var restoredRich = await reopened.Medicines.AsNoTracking().SingleAsync(m => m.Name == "Metformin");
            var restoredBare = await reopened.Medicines.AsNoTracking().SingleAsync(m => m.Name == "Aspirin");

            // Covers both a fully-populated row and one that leaves every
            // optional field null (schema fidelity, §8.1).
            restoredRich.Should().BeEquivalentTo(originalRich);
            restoredBare.Should().BeEquivalentTo(originalBare);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Import_with_wrong_passphrase_fails_and_leaves_the_target_untouched()
    {
        await SeedAsync();

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            int before;
            await using (var db = CreateProfileContext())
            {
                before = await db.Medicines.CountAsync();
            }

            await using (var live = CreateProfileContext())
            {
                var import = CreateImportService(live, new FakeCredentialProtector());
                var act = () => import.ImportAsync(
                    archivePath, "definitely the wrong one".ToCharArray(),
                    new ImportOptions(), null, CancellationToken.None);

                var ex = await act.Should().ThrowAsync<ImportFailedException>();
                ex.Which.Reason.Should().Be(ImportFailureReason.WrongPassphrase);
            }

            await using var reopened = CreateProfileContext();
            (await reopened.Medicines.CountAsync()).Should().Be(before);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Import_with_a_tampered_payload_fails_like_a_wrong_passphrase()
    {
        await SeedAsync();

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            FlipOnePayloadByte(archivePath);

            await using var live = CreateProfileContext();
            var import = CreateImportService(live, new FakeCredentialProtector());
            var act = () => import.ImportAsync(
                archivePath, Passphrase.ToCharArray(),
                new ImportOptions(), null, CancellationToken.None);

            // A GCM tag mismatch is indistinguishable from a wrong
            // passphrase from the outside (§4.4).
            var ex = await act.Should().ThrowAsync<ImportFailedException>();
            ex.Which.Reason.Should().Be(ImportFailureReason.WrongPassphrase);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Import_of_a_truncated_archive_reports_corrupt()
    {
        await SeedAsync();

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            var bytes = await File.ReadAllBytesAsync(archivePath);
            await File.WriteAllBytesAsync(archivePath, bytes[..(bytes.Length / 2)]);

            await using var live = CreateProfileContext();
            var import = CreateImportService(live, new FakeCredentialProtector());
            var act = () => import.ImportAsync(
                archivePath, Passphrase.ToCharArray(),
                new ImportOptions(), null, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ImportFailedException>();
            ex.Which.Reason.Should().Be(ImportFailureReason.Corrupt);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Import_refuses_a_newer_format_version()
    {
        await SeedAsync();

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            RewriteManifest(archivePath, m => m["formatVersion"] = ExportFormat.CurrentFormatVersion + 1);

            await using var live = CreateProfileContext();
            var import = CreateImportService(live, new FakeCredentialProtector());
            var act = () => import.ImportAsync(
                archivePath, Passphrase.ToCharArray(),
                new ImportOptions(), null, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ImportFailedException>();
            ex.Which.Reason.Should().Be(ImportFailureReason.UnsupportedVersion);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Import_refuses_a_newer_schema_version()
    {
        await SeedAsync();

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            // Bump the payload's schemaVersion and re-seal so only the
            // schema-version guard trips (not the hash or the cipher).
            RepackPayload(archivePath, payload =>
                payload["schemaVersion"] = ExportFormat.CurrentSchemaVersion + 1);

            await using var live = CreateProfileContext();
            var import = CreateImportService(live, new FakeCredentialProtector());
            var act = () => import.ImportAsync(
                archivePath, Passphrase.ToCharArray(),
                new ImportOptions(), null, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ImportFailedException>();
            ex.Which.Reason.Should().Be(ImportFailureReason.UnsupportedVersion);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Import_of_an_older_style_payload_takes_defaults_for_missing_fields()
    {
        await SeedAsync();

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            // Simulate an archive produced by an older build that did not
            // yet have the A5 "remindOnDose" field: strip it from every
            // medicine. On import it must take its default (false),
            // not fail (§4.3 — older schema imports).
            RepackPayload(archivePath, payload =>
            {
                foreach (var med in payload["medicines"]!.AsArray())
                {
                    med!.AsObject().Remove("remindOnDose");
                }
            });

            await WipeProfileDatabaseAsync();

            await using (var live = CreateProfileContext())
            {
                var import = CreateImportService(live, new FakeCredentialProtector());
                await import.ImportAsync(
                    archivePath, Passphrase.ToCharArray(),
                    new ImportOptions(), null, CancellationToken.None);
            }

            await using var reopened = CreateProfileContext();
            var medicines = await reopened.Medicines.AsNoTracking().ToListAsync();
            medicines.Should().NotBeEmpty();
            medicines.Should().OnlyContain(m => m.RemindOnDose == false);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Export_refuses_a_passphrase_below_the_minimum_before_writing()
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
    public async Task Automatic_source_annotates_manifest_with_source_and_hashed_hostname()
    {
        // C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.6):
        // when AutomaticSource is true the manifest must carry
        // Source = "automatic" and a Device block with a SHA-256 hex
        // hash of the machine's host name.
        await SeedAsync();

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions
                {
                    DestinationPath = archivePath,
                    AutomaticSource = true,
                },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            await using var live = CreateProfileContext();
            var import = CreateImportService(live, new FakeCredentialProtector());
            var manifest = await import.ReadManifestAsync(archivePath, CancellationToken.None);

            manifest.Source.Should().Be("automatic");
            manifest.Device.Should().NotBeNull();
            manifest.Device!.HostNameSha256.Should().HaveLength(64)
                .And.MatchRegex("^[0-9a-f]+$");
            manifest.Device.HostNameSha256.Should().NotContain(Environment.MachineName);
            manifest.Device.ProfileId.Should().Be(_profileId);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task User_source_leaves_source_and_device_null()
    {
        await SeedAsync();

        var archivePath = NewArchivePath();
        var export = CreateExportService(new FakeCredentialStore());
        try
        {
            await export.ExportAsync(
                new ExportOptions { DestinationPath = archivePath },
                Passphrase.ToCharArray(), null, CancellationToken.None);

            await using var live = CreateProfileContext();
            var import = CreateImportService(live, new FakeCredentialProtector());
            var manifest = await import.ReadManifestAsync(archivePath, CancellationToken.None);

            manifest.Source.Should().BeNull();
            manifest.Device.Should().BeNull();
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Smtp_password_round_trips_through_the_archive_key()
    {
        await SeedAsync();

        var archivePath = NewArchivePath();
        var credentialStore = new FakeCredentialStore { Password = "s3cr3t-smtp-pw" };
        var export = CreateExportService(credentialStore);
        WriteSmtpSettingsFile();
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

            await WipeProfileDatabaseAsync();

            var protector = new FakeCredentialProtector();
            await using (var live = CreateProfileContext())
            {
                var import = CreateImportService(live, protector);
                await import.ImportAsync(
                    archivePath, Passphrase.ToCharArray(),
                    new ImportOptions(), null, CancellationToken.None);
            }

            // smtp.protected is written under the fixture's shared dir,
            // holding the (fake-)DPAPI ciphertext of the round-tripped
            // password.
            var credentialsPath = Path.Combine(_sharedDirectory, AppDataPaths.CredentialsFileName);
            File.Exists(credentialsPath).Should().BeTrue();
            var stored = await File.ReadAllTextAsync(credentialsPath);
            protector.Unprotect(stored).Should().Be("s3cr3t-smtp-pw");
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    // -------- infrastructure --------

    private static string NewArchivePath()
        => Path.Combine(
            Path.GetTempPath(),
            "mr-e2e-" + Guid.NewGuid().ToString("N") + ExportFormat.ArchiveExtension);

    private ExportService CreateExportService(ISmtpCredentialStore credentialStore)
    {
        var backupService = new BackupService(
            CreateProfileContext(), TimeProvider.System,
            new DatabasePathProvider(_profile.DatabasePath));
        return new ExportService(
            _profile, backupService, _cipher, credentialStore, TimeProvider.System,
            NullLogger<ExportService>.Instance, _sharedDirectory);
    }

    private ImportService CreateImportService(
        MedReminderDbContext live, ICredentialProtector protector)
        => new(
            _profile, live, _cipher, protector, TimeProvider.System,
            NullLogger<ImportService>.Instance, _sharedDirectory);

    private MedReminderDbContext CreateProfileContext()
    {
        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(AppDataPaths.BuildSqliteConnectionString(_profile.DatabasePath))
            .Options;
        return new MedReminderDbContext(options);
    }

    // FK-safe truncation: delete dependents before their parent Medicine.
    private async Task WipeProfileDatabaseAsync()
    {
        await using (var db = CreateProfileContext())
        {
            await db.DoseReminderEvents.ExecuteDeleteAsync();
            await db.NotificationEvents.ExecuteDeleteAsync();
            await db.MedicationIntakes.ExecuteDeleteAsync();
            await db.MedicationSuspensions.ExecuteDeleteAsync();
            await db.MedicationAdministrationSlots.ExecuteDeleteAsync();
            await db.MedicationScheduleHistories.ExecuteDeleteAsync();
            await db.StockMovements.ExecuteDeleteAsync();
            await db.Medicines.ExecuteDeleteAsync();
        }

        // Release the pool so ImportService can swap the DB file.
        SqliteConnection.ClearAllPools();
    }

    private async Task SeedAsync()
    {
        await using var db = CreateProfileContext();
        await db.Database.EnsureCreatedAsync();

        var medicineId = Guid.NewGuid();
        db.Medicines.Add(new Medicine
        {
            Id = medicineId,
            Name = "Metformin",
            ActiveIngredient = "metformin hydrochloride",
            Package = "60 tablets",
            Unit = "tablets",
            DosePerAdministration = 1.5m,
            AdministrationsPerDay = 2,
            StartDate = new DateOnly(2026, 1, 10),
            EndDate = new DateOnly(2026, 12, 31),
            ThresholdDays = 7,
            DoctorName = "Dr. Rossi",
            Notes = "with meals",
            IsActive = true,
            StockEpoch = 3,
            NotificationChannels = NotificationChannels.Both,
            CreatedAt = new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 2, 1, 9, 30, 0, TimeSpan.Zero),
            RemindOnDose = true,
            NationalCode = "012345",
            AtcCode = MedReminder.Domain.Catalogue.AtcCode.Parse("A10BA02"),
            LinkedReferenceMedicineId = Guid.NewGuid(),
        });

        // Every optional field left at default / null.
        db.Medicines.Add(new Medicine
        {
            Id = Guid.NewGuid(),
            Name = "Aspirin",
            Unit = "tablets",
            DosePerAdministration = 1m,
            AdministrationsPerDay = 1,
            StartDate = new DateOnly(2026, 3, 1),
            ThresholdDays = 0,
            IsActive = false,
            StockEpoch = 1,
            NotificationChannels = NotificationChannels.None,
            CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
        });

        db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            OccurredAt = new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero),
            Kind = StockMovementKind.InitialLoad,
            QuantityDelta = 60m,
            StockEpoch = 1,
            Notes = "first pack",
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
            ScheduledAt = new DateTimeOffset(2026, 1, 11, 8, 30, 0, TimeSpan.Zero),
            ActualAt = new DateTimeOffset(2026, 1, 11, 8, 45, 0, TimeSpan.Zero),
            Quantity = 1.5m,
            Status = IntakeStatus.Taken,
            Notes = "ok",
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

    // -------- archive manipulation helpers --------

    private static void FlipOnePayloadByte(string archivePath)
    {
        var (manifest, payload) = ReadEntries(archivePath);
        payload[payload.Length / 2] ^= 0xFF;
        WriteArchive(archivePath, manifest, payload);
    }

    private static void RewriteManifest(string archivePath, Action<JsonObject> mutate)
    {
        var (manifestBytes, payload) = ReadEntries(archivePath);
        var manifest = JsonNode.Parse(manifestBytes)!.AsObject();
        mutate(manifest);
        WriteArchive(archivePath, Encoding.UTF8.GetBytes(manifest.ToJsonString()), payload);
    }

    // Decrypts the payload, applies mutate to its JSON, re-encrypts with
    // the same passphrase + KDF, and updates the manifest cipher/hash so
    // only the intended field changes.
    private void RepackPayload(string archivePath, Action<JsonObject> mutate)
    {
        var (manifestBytes, ciphertext) = ReadEntries(archivePath);
        var manifest = JsonNode.Parse(manifestBytes)!.AsObject();

        var kdf = manifest["kdf"]!.AsObject();
        var salt = Convert.FromBase64String((string)kdf["saltBase64"]!);
        var kdfParams = new Argon2Params
        {
            Iterations = (int)kdf["iterations"]!,
            MemoryKiB = (int)kdf["memoryKiB"]!,
            Parallelism = (int)kdf["parallelism"]!,
        };
        var key = _cipher.DeriveKey(Passphrase.ToCharArray(), salt, kdfParams);
        try
        {
            var cipherNode = manifest["cipher"]!.AsObject();
            var nonce = Convert.FromBase64String((string)cipherNode["nonceBase64"]!);
            var tag = Convert.FromBase64String((string)cipherNode["tagBase64"]!);
            var plaintext = _cipher.Decrypt(key, nonce, tag, ciphertext);

            var payload = JsonNode.Parse(plaintext)!.AsObject();
            mutate(payload);
            var newPlaintext = Encoding.UTF8.GetBytes(payload.ToJsonString());

            var (newNonce, newTag, newCiphertext) = _cipher.Encrypt(key, newPlaintext);
            cipherNode["nonceBase64"] = Convert.ToBase64String(newNonce);
            cipherNode["tagBase64"] = Convert.ToBase64String(newTag);
            var payloadNode = manifest["payload"]!.AsObject();
            payloadNode["sha256Base64"] = Convert.ToBase64String(SHA256.HashData(newPlaintext));
            payloadNode["sizeBytes"] = newPlaintext.Length;

            WriteArchive(archivePath, Encoding.UTF8.GetBytes(manifest.ToJsonString()), newCiphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static (byte[] Manifest, byte[] Payload) ReadEntries(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        return (ReadEntry(archive, ExportFormat.ManifestEntryName),
                ReadEntry(archive, ExportFormat.PayloadEntryName));
    }

    private static byte[] ReadEntry(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void WriteArchive(string path, byte[] manifestBytes, byte[] payloadBytes)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        using (var m = archive.CreateEntry(ExportFormat.ManifestEntryName).Open())
        {
            m.Write(manifestBytes);
        }
        using var p = archive.CreateEntry(ExportFormat.PayloadEntryName).Open();
        p.Write(payloadBytes);
    }

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
        try { Directory.Delete(_sharedDirectory, recursive: true); } catch { /* best effort */ }
    }

    private sealed record CapturedCounts(
        int Medicines, int StockMovements, int Schedules, int Slots,
        int Suspensions, int Intakes, int NotificationEvents, int DoseReminderEvents)
    {
        public static async Task<CapturedCounts> FromAsync(MedReminderDbContext db) => new(
            await db.Medicines.CountAsync(),
            await db.StockMovements.CountAsync(),
            await db.MedicationScheduleHistories.CountAsync(),
            await db.MedicationAdministrationSlots.CountAsync(),
            await db.MedicationSuspensions.CountAsync(),
            await db.MedicationIntakes.CountAsync(),
            await db.NotificationEvents.CountAsync(),
            await db.DoseReminderEvents.CountAsync());
    }

    private sealed class FakeCredentialStore : ISmtpCredentialStore
    {
        public string? Password { get; set; }
        public bool HasPassword => !string.IsNullOrEmpty(Password);
        public string? GetPassword() => Password;
        public void SetPassword(string password) => Password = password;
        public void Clear() => Password = null;
    }

    // Reversible stand-in for DPAPI so the SMTP-password test does not
    // depend on the ambient Windows account. The production path uses
    // DpapiCredentialProtector; this only checks the archive-key <->
    // protector round-trip logic in ImportService.
    private sealed class FakeCredentialProtector : ICredentialProtector
    {
        public string Protect(string plaintext)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes("dpapi:" + plaintext));

        public string Unprotect(string ciphertext)
            => Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext))["dpapi:".Length..];
    }

    private sealed class FakeCurrentProfile : ICurrentProfile
    {
        public FakeCurrentProfile(string id)
        {
            Id = id;
            DataDirectory = AppDataPaths.GetProfileDataDirectory(id);
            DatabasePath = Path.Combine(DataDirectory, AppDataPaths.DatabaseFileName);
            NotificationSettingsPath = Path.Combine(DataDirectory, "notifications.settings.json");
        }

        public string Id { get; }
        public string DisplayName => "Test Profile";
        public ProfileRole Role => ProfileRole.Admin;
        public bool IsAdmin => true;
        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string NotificationSettingsPath { get; }
    }
}
