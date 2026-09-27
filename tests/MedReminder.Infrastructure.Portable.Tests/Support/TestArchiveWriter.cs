using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Export;

namespace MedReminder.Infrastructure.Tests.Support;

// Writes a .mrz archive the way ExportService does (docs/EXPORT-FORMAT.md
// §1, §2, §4), without the Windows-only snapshot and settings steps, so
// the portable reader can be tested on any OS. Cheap Argon2 parameters
// keep the tests fast; the reader honours whatever the manifest declares.
internal static class TestArchiveWriter
{
    public static readonly Argon2Params FastKdf = new() { Iterations = 1, MemoryKiB = 1024, Parallelism = 1 };

    public static byte[] Write(
        ExportPayload payload,
        string passphrase,
        string? smtpPassword = null,
        Action<ExportManifest>? tamperManifest = null,
        Func<byte[], byte[]>? tamperCiphertext = null,
        bool omitPayloadEntry = false,
        IReadOnlyDictionary<string, string>? extraEntries = null)
    {
        var cipher = new ArchiveCipher();
        var salt = RandomNumberGenerator.GetBytes(ExportFormat.SaltSizeBytes);
        var key = cipher.DeriveKey(passphrase.ToCharArray(), salt, FastKdf);

        if (smtpPassword is not null)
        {
            var (sn, st, sc) = cipher.Encrypt(key, Encoding.UTF8.GetBytes(smtpPassword));
            payload.Shared.SmtpPasswordEncrypted = new ExportedProtectedSecret
            {
                NonceBase64 = Convert.ToBase64String(sn),
                TagBase64 = Convert.ToBase64String(st),
                CiphertextBase64 = Convert.ToBase64String(sc),
            };
        }

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, ExportJson.Options);
        var (nonce, tag, ciphertext) = cipher.Encrypt(key, payloadBytes);
        CryptographicOperations.ZeroMemory(key);

        var manifest = new ExportManifest
        {
            AppVersion = "test",
            CreatedAtUtc = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero),
            ProfileId = payload.Profile.Id,
            Kdf = new ManifestKdf
            {
                Iterations = FastKdf.Iterations,
                MemoryKiB = FastKdf.MemoryKiB,
                Parallelism = FastKdf.Parallelism,
                SaltBase64 = Convert.ToBase64String(salt),
            },
            Cipher = new ManifestCipher
            {
                NonceBase64 = Convert.ToBase64String(nonce),
                TagBase64 = Convert.ToBase64String(tag),
            },
            Payload = new ManifestPayload
            {
                Sha256Base64 = Convert.ToBase64String(SHA256.HashData(payloadBytes)),
                SizeBytes = payloadBytes.Length,
            },
        };
        tamperManifest?.Invoke(manifest);
        if (tamperCiphertext is not null) ciphertext = tamperCiphertext(ciphertext);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = zip.CreateEntry(ExportFormat.ManifestEntryName).Open())
                JsonSerializer.Serialize(entry, manifest, ExportJson.Options);
            if (!omitPayloadEntry)
            {
                using var entry = zip.CreateEntry(ExportFormat.PayloadEntryName).Open();
                entry.Write(ciphertext);
            }
            foreach (var (name, content) in extraEntries ?? new Dictionary<string, string>())
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }
        return buffer.ToArray();
    }

    public static ExportPayload SamplePayload()
    {
        var medicineId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var payload = new ExportPayload
        {
            Profile = new ExportedProfileInfo
            {
                Id = "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f",
                DisplayName = "Test profile",
                Role = "Admin",
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
            NotificationSettings = new ExportedNotificationSettings { ToAddress = "user@example.org" },
        };
        payload.Medicines.Add(new ExportedMedicine
        {
            Id = medicineId,
            Name = "Sample",
            Unit = "tablet",
            DosePerAdministration = 1m,
            AdministrationsPerDay = 2,
            StartDate = new DateOnly(2026, 9, 1),
            ThresholdDays = 7,
            IsActive = true,
            StockEpoch = 2,
            NotificationChannels = "Windows",
            CreatedAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
        });
        payload.StockMovements.Add(new ExportedStockMovement
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            OccurredAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            Kind = "InitialLoad",
            QuantityDelta = 30m,
            StockEpoch = 1,
        });
        payload.StockMovements.Add(new ExportedStockMovement
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            OccurredAt = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
            Kind = "Consumption",
            QuantityDelta = -2m,
            StockEpoch = 1,
        });
        payload.MedicationScheduleHistory.Add(new ExportedScheduleHistory
        {
            Id = Guid.NewGuid(),
            MedicineId = medicineId,
            EffectiveFrom = new DateOnly(2026, 9, 1),
            DosePerAdministration = 1m,
            AdministrationsPerDay = 2,
            ScheduleKind = "FixedDaily",
        });
        return payload;
    }
}
