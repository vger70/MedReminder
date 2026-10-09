using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MedReminder.Application.Export;

namespace MedReminder.Infrastructure.Export;

// What goes into one archive besides the payload: the manifest fields
// the caller decides.
internal sealed class ArchiveContent
{
    public required ExportPayload Payload { get; init; }

    public required string ProfileId { get; init; }

    public required string AppVersion { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public ManifestIncludes Includes { get; init; } = new();

    // C.3+ automatic cloud snapshots only (ANALYSIS-C3PLUS-CLOUD-BACKUP
    // §3.6); null for a user export.
    public string? Source { get; init; }

    public ManifestDevice? Device { get; init; }

    // The opt-in SMTP password as UTF-8 bytes, encrypted with the archive
    // key (§3.4). Owned by the caller, which zeroes it.
    public byte[]? SmtpPassword { get; init; }
}

// Platform-neutral write half of the .mrz export
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.1 steps 5-10),
// extracted from ExportService so that a mobile host writes the same
// format: serialize the payload, derive the key from a fresh salt,
// AES-GCM-encrypt, hash, build the manifest and write the ZIP to a
// stream (a file on Windows, a document the user picked on Android).
//
// Security (CLAUDE.md §7): the passphrase, the derived key and the
// payload plaintext are never logged; the key is zeroed after use.
internal static class ArchiveWriter
{
    // §4.5: checked before anything is read or written, so a rejected
    // export never leaves a partial file.
    public static void ValidatePassphrase(char[] passphrase)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        if (passphrase.Length < ExportFormat.MinPassphraseLength)
        {
            throw new ExportValidationException(
                ExportValidationReason.PassphraseTooShort,
                $"The passphrase must be at least {ExportFormat.MinPassphraseLength} characters.");
        }
    }

    public static void Write(Stream destination, ArchiveContent content, char[] passphrase, IArchiveCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(cipher);
        ValidatePassphrase(passphrase);

        var payload = content.Payload;
        var includes = content.Includes;
        var salt = RandomNumberGenerator.GetBytes(ExportFormat.SaltSizeBytes);
        var kdfParams = Argon2Params.Default;
        var key = cipher.DeriveKey(passphrase, salt, kdfParams);
        try
        {
            if (content.SmtpPassword is { Length: > 0 } password)
            {
                var (secretNonce, secretTag, secretCiphertext) = cipher.Encrypt(key, password);
                payload.Shared.SmtpPasswordEncrypted = new ExportedProtectedSecret
                {
                    NonceBase64 = Convert.ToBase64String(secretNonce),
                    TagBase64 = Convert.ToBase64String(secretTag),
                    CiphertextBase64 = Convert.ToBase64String(secretCiphertext),
                };
                includes.SmtpCredential = true;
            }

            // payload.json: UTF-8, no BOM, camelCase.
            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, ExportJson.Options);
            try
            {
                var (nonce, tag, ciphertext) = cipher.Encrypt(key, payloadBytes);
                var manifest = new ExportManifest
                {
                    AppVersion = content.AppVersion,
                    CreatedAtUtc = content.CreatedAtUtc,
                    Scope = "profile",
                    ProfileId = content.ProfileId,
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
                        Sha256Base64 = Convert.ToBase64String(SHA256.HashData(payloadBytes)),
                        SizeBytes = payloadBytes.Length,
                    },
                    Includes = includes,
                    Source = content.Source,
                    Device = content.Device,
                };

                WriteZip(destination, manifest, ciphertext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payloadBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    // manifest.json in clear, payload.enc encrypted. The stream stays
    // open: its owner closes it.
    private static void WriteZip(Stream destination, ExportManifest manifest, byte[] ciphertext)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        var manifestEntry = archive.CreateEntry(ExportFormat.ManifestEntryName, CompressionLevel.Optimal);
        using (var manifestStream = manifestEntry.Open())
        {
            manifestStream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, ExportJson.Options));
        }

        var payloadEntry = archive.CreateEntry(ExportFormat.PayloadEntryName, CompressionLevel.Optimal);
        using var payloadStream = payloadEntry.Open();
        payloadStream.Write(ciphertext);
    }
}
