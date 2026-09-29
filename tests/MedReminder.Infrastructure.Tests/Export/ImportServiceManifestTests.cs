using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Export;

// ImportService coverage for C.3 Step 5 that does not require a full
// export round-trip (that lands in Step 6). Focuses on manifest reading
// and the pre-decryption failure surfaces (§4.3, §4.4): unsupported
// version, corrupt / truncated archive, missing entries. Archives here
// are hand-built so the tests do not depend on ExportService.
[SupportedOSPlatform("windows")]
public sealed class ImportServiceManifestTests : IDisposable
{
    private readonly string _profileId;
    private readonly FakeCurrentProfile _profile;
    private readonly string _sharedDirectory;
    private readonly IArchiveCipher _cipher = new ArchiveCipher();

    private const string Passphrase = "correct horse battery staple";

    public ImportServiceManifestTests()
    {
        _profileId = Guid.NewGuid().ToString("N");
        _profile = new FakeCurrentProfile(_profileId);
        _sharedDirectory = Path.Combine(
            Path.GetTempPath(), "mr-import-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sharedDirectory);
    }

    [Fact]
    public async Task ReadManifest_returns_the_declared_metadata()
    {
        var archivePath = BuildArchive(out var manifest, ExportFormat.CurrentSchemaVersion);
        try
        {
            var import = CreateImportService(out var live);
            using (live)
            {
                var read = await import.ReadManifestAsync(archivePath, CancellationToken.None);
                read.Format.Should().Be(ExportFormat.FormatIdentifier);
                read.FormatVersion.Should().Be(manifest.FormatVersion);
                read.ProfileId.Should().Be(manifest.ProfileId);
                read.Includes.SmtpSettings.Should().Be(manifest.Includes.SmtpSettings);
            }
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task ReadManifest_rejects_a_newer_format_version()
    {
        var archivePath = BuildArchive(
            out _, ExportFormat.CurrentSchemaVersion,
            mutateManifest: m => m.FormatVersion = ExportFormat.CurrentFormatVersion + 1);
        try
        {
            var import = CreateImportService(out var live);
            using (live)
            {
                var act = () => import.ReadManifestAsync(archivePath, CancellationToken.None);
                var ex = await act.Should().ThrowAsync<ImportFailedException>();
                ex.Which.Reason.Should().Be(ImportFailureReason.UnsupportedVersion);
            }
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task ReadManifest_rejects_a_non_medreminder_archive()
    {
        var archivePath = BuildArchive(
            out _, ExportFormat.CurrentSchemaVersion,
            mutateManifest: m => m.Format = "something-else");
        try
        {
            var import = CreateImportService(out var live);
            using (live)
            {
                var act = () => import.ReadManifestAsync(archivePath, CancellationToken.None);
                var ex = await act.Should().ThrowAsync<ImportFailedException>();
                ex.Which.Reason.Should().Be(ImportFailureReason.Corrupt);
            }
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task ReadManifest_rejects_a_zip_missing_the_payload_entry()
    {
        var archivePath = Path.Combine(
            Path.GetTempPath(), "mr-nopayload-" + Guid.NewGuid().ToString("N") + ".mrz");
        var manifest = BuildManifest(ExportFormat.CurrentSchemaVersion);
        using (var stream = new FileStream(archivePath, FileMode.Create))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            using var m = archive.CreateEntry(ExportFormat.ManifestEntryName).Open();
            m.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, ExportJson.Options));
        }
        try
        {
            var import = CreateImportService(out var live);
            using (live)
            {
                var act = () => import.ReadManifestAsync(archivePath, CancellationToken.None);
                var ex = await act.Should().ThrowAsync<ImportFailedException>();
                ex.Which.Reason.Should().Be(ImportFailureReason.Corrupt);
            }
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Import_of_a_non_zip_file_reports_corrupt()
    {
        var archivePath = Path.Combine(
            Path.GetTempPath(), "mr-garbage-" + Guid.NewGuid().ToString("N") + ".mrz");
        await File.WriteAllTextAsync(archivePath, "this is not a zip file");
        try
        {
            var import = CreateImportService(out var live);
            using (live)
            {
                var act = () => import.ImportAsync(
                    archivePath, Passphrase.ToCharArray(), new ImportOptions(),
                    null, CancellationToken.None);
                var ex = await act.Should().ThrowAsync<ImportFailedException>();
                ex.Which.Reason.Should().Be(ImportFailureReason.Corrupt);
            }
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    [Fact]
    public async Task Import_of_a_missing_file_reports_corrupt()
    {
        var archivePath = Path.Combine(
            Path.GetTempPath(), "mr-missing-" + Guid.NewGuid().ToString("N") + ".mrz");

        var import = CreateImportService(out var live);
        using (live)
        {
            var act = () => import.ImportAsync(
                archivePath, Passphrase.ToCharArray(), new ImportOptions(),
                null, CancellationToken.None);
            var ex = await act.Should().ThrowAsync<ImportFailedException>();
            ex.Which.Reason.Should().Be(ImportFailureReason.Corrupt);
        }
    }

    // -------- helpers --------

    // Builds a well-formed archive whose payload is a valid encrypted
    // ExportPayload with the given schemaVersion. mutateManifest lets a
    // test corrupt a single manifest field after it is built.
    private string BuildArchive(
        out ExportManifest manifest,
        int schemaVersion,
        Action<ExportManifest>? mutateManifest = null)
    {
        var payload = new ExportPayload { SchemaVersion = schemaVersion };
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, ExportJson.Options);

        var salt = RandomNumberGenerator.GetBytes(ExportFormat.SaltSizeBytes);
        var kdf = Argon2Params.Default;
        var key = _cipher.DeriveKey(Passphrase.ToCharArray(), salt, kdf);
        try
        {
            var (nonce, tag, ciphertext) = _cipher.Encrypt(key, payloadBytes);
            manifest = new ExportManifest
            {
                AppVersion = "test",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Scope = "profile",
                ProfileId = _profileId,
                Kdf = new ManifestKdf
                {
                    Iterations = kdf.Iterations,
                    MemoryKiB = kdf.MemoryKiB,
                    Parallelism = kdf.Parallelism,
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
                Includes = new ManifestIncludes(),
            };
            mutateManifest?.Invoke(manifest);

            var archivePath = Path.Combine(
                Path.GetTempPath(), "mr-import-" + Guid.NewGuid().ToString("N") + ".mrz");
            using var stream = new FileStream(archivePath, FileMode.Create);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            using (var m = archive.CreateEntry(ExportFormat.ManifestEntryName).Open())
            {
                m.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, ExportJson.Options));
            }
            using (var p = archive.CreateEntry(ExportFormat.PayloadEntryName).Open())
            {
                p.Write(ciphertext);
            }
            return archivePath;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private ExportManifest BuildManifest(int schemaVersion)
    {
        var payload = new ExportPayload { SchemaVersion = schemaVersion };
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, ExportJson.Options);
        return new ExportManifest
        {
            AppVersion = "test",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Scope = "profile",
            ProfileId = _profileId,
            Payload = new ManifestPayload
            {
                Sha256Base64 = Convert.ToBase64String(SHA256.HashData(payloadBytes)),
                SizeBytes = payloadBytes.Length,
            },
        };
    }

    private ImportService CreateImportService(out MedReminderDbContext live)
    {
        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(AppDataPaths.BuildSqliteConnectionString(_profile.DatabasePath))
            .Options;
        live = new MedReminderDbContext(options);
        return new ImportService(
            _profile, live, _cipher, new FakeCredentialProtector(),
            TimeProvider.System, NullLogger<ImportService>.Instance, _sharedDirectory);
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

    private sealed class FakeCredentialProtector : ICredentialProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
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
