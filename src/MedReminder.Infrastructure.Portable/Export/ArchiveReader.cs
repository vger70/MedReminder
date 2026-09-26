using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MedReminder.Application.Export;

namespace MedReminder.Infrastructure.Export;

// Platform-neutral read half of the .mrz import
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.2 steps 1-6), extracted
// from ImportService so that a mobile host can reuse it
// (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §13 Phase 1). Reads from a
// stream, never touches the file system or a database.
//
// Security (CLAUDE.md §7): the passphrase, the derived key and the
// payload plaintext are never logged; the plaintext buffer is zeroed
// after parsing, the key is zeroed by DecryptedArchive.Dispose (or here
// on failure).
internal sealed class ArchiveReader : IArchiveReader
{
    private const string CorruptMessage = "The export file is damaged and cannot be imported.";
    private const string NewerVersionMessage =
        "This export was produced by a newer version of MedReminder. Update the app and try again.";

    private readonly IArchiveCipher _cipher;

    public ArchiveReader(IArchiveCipher cipher)
    {
        _cipher = cipher;
    }

    public ExportManifest ReadManifest(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        return Open(archive).Manifest;
    }

    public DecryptedArchive Decrypt(Stream archive, char[] passphrase)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(passphrase);

        // Steps 1-2: open the ZIP, parse and version-check the manifest.
        var (manifest, ciphertext) = Open(archive);

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
            var salt = DecodeBase64(manifest.Kdf.SaltBase64);
            key = _cipher.DeriveKey(passphrase, salt, kdfParams);

            // Step 4: decrypt. A tag mismatch is the wrong-passphrase
            // surface (§4.4).
            var nonce = DecodeBase64(manifest.Cipher.NonceBase64);
            var tag = DecodeBase64(manifest.Cipher.TagBase64);
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

            try
            {
                // Step 5: verify the payload hash.
                var expectedHash = DecodeBase64(manifest.Payload.Sha256Base64);
                var actualHash = SHA256.HashData(plaintext);
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                    throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage);

                // Step 6: parse payload.json and check the schema version.
                ExportPayload? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<ExportPayload>(plaintext, ExportJson.Options);
                }
                catch (JsonException ex)
                {
                    throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage, ex);
                }

                if (payload is null)
                    throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage);

                if (payload.SchemaVersion > ExportFormat.CurrentSchemaVersion)
                    throw new ImportFailedException(ImportFailureReason.UnsupportedVersion, NewerVersionMessage);

                var result = new DecryptedArchive(manifest, payload, key, _cipher);
                key = null; // ownership moved to the result
                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    // Opens the archive, reads manifest.json and the payload.enc bytes,
    // and enforces the format identifier and version guards (§4.3).
    private static (ExportManifest Manifest, byte[] Ciphertext) Open(Stream archiveStream)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage, ex);
        }

        using (archive)
        {
            var manifestEntry = archive.GetEntry(ExportFormat.ManifestEntryName);
            var payloadEntry = archive.GetEntry(ExportFormat.PayloadEntryName);
            if (manifestEntry is null || payloadEntry is null)
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage);

            ExportManifest? manifest;
            try
            {
                using var manifestStream = manifestEntry.Open();
                manifest = JsonSerializer.Deserialize<ExportManifest>(manifestStream, ExportJson.Options);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage, ex);
            }

            if (manifest is null
                || !string.Equals(manifest.Format, ExportFormat.FormatIdentifier, StringComparison.Ordinal))
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage);

            if (manifest.FormatVersion > ExportFormat.CurrentFormatVersion)
                throw new ImportFailedException(ImportFailureReason.UnsupportedVersion, NewerVersionMessage);

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
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage, ex);
            }

            return (manifest, ciphertext);
        }
    }

    private static byte[] DecodeBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage, ex);
        }
    }
}
