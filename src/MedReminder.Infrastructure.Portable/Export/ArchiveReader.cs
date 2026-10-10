using System.Buffers.Binary;
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
    private const string TooLargeMessage = "The export file is larger than any MedReminder export and cannot be imported.";
    private const string NewerVersionMessage =
        "This export was produced by a newer version of MedReminder. Update the app and try again.";

    // Size limits against a crafted archive (a ZIP bomb, an oversized
    // file): an export holds text records only, a few MB even after years
    // of use, so these leave ample room. The ciphertext is about the size
    // of the payload JSON; the archive adds the manifest and ZIP headers.
    internal const int MaxManifestBytes = 64 * 1024;
    internal const int MaxPayloadBytes = 64 * 1024 * 1024;
    internal const int MaxArchiveBytes = MaxPayloadBytes + 1024 * 1024;

    // An export has two entries. ZipArchive indexes every entry on the
    // first lookup, at a few hundred bytes of memory each, so within the
    // size limit a crafted archive of tiny entries could cost hundreds of
    // MiB before the two expected ones are even looked for. The declared
    // count is checked first; ZipArchive reads no more entries than that
    // and fails as soon as it finds others. The margin tolerates files
    // added by other tools.
    internal const int MaxEntries = 16;

    private const int EndOfCentralDirectorySize = 22;
    private const uint EndOfCentralDirectorySignature = 0x06054b50;

    private readonly IArchiveCipher _cipher;

    public ArchiveReader(IArchiveCipher cipher)
    {
        _cipher = cipher;
    }

    public ExportManifest ReadManifest(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        // The payload is not needed for a preview: it is not decompressed.
        return Open(archive, readPayload: false).Manifest;
    }

    public DecryptedArchive Decrypt(Stream archive, char[] passphrase)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(passphrase);

        // Steps 1-2: open the ZIP, parse and version-check the manifest.
        var (manifest, ciphertext) = Open(archive, readPayload: true);

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
            var salt = DecodeBase64(manifest.Kdf.SaltBase64, ExportFormat.SaltSizeBytes);
            try
            {
                // The cipher refuses a cost outside Argon2Params' limits
                // before deriving: a crafted manifest is a damaged file.
                key = _cipher.DeriveKey(passphrase, salt, kdfParams);
            }
            catch (InvalidDataException ex)
            {
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage, ex);
            }

            // Step 4: decrypt. A tag mismatch is the wrong-passphrase
            // surface (§4.4).
            var nonce = DecodeBase64(manifest.Cipher.NonceBase64, ExportFormat.AesGcmNonceSizeBytes);
            var tag = DecodeBase64(manifest.Cipher.TagBase64, ExportFormat.AesGcmTagSizeBytes);
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

    // Opens the archive, reads manifest.json and, when asked, the
    // payload.enc bytes, and enforces the format identifier and version
    // guards (§4.3).
    private static (ExportManifest Manifest, byte[] Ciphertext) Open(Stream archiveStream, bool readPayload)
    {
        ZipArchive archive;
        try
        {
            // ZipArchive buffers a non-seekable stream (the mobile picker)
            // whole: read it here, within the limit, instead.
            if (archiveStream.CanSeek)
            {
                if (archiveStream.Length - archiveStream.Position > MaxArchiveBytes)
                    throw new ImportFailedException(ImportFailureReason.Corrupt, TooLargeMessage);
            }
            else
            {
                var bytes = ReadBounded(archiveStream, MaxArchiveBytes, sizeHint: 0, out var length);
                archiveStream = new MemoryStream(bytes, 0, length, writable: false);
            }
            if (DeclaredEntries(archiveStream) > MaxEntries)
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage);
            archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage, ex);
        }

        using (archive)
        {
            ZipArchiveEntry? manifestEntry, payloadEntry;
            try
            {
                // The first lookup reads the central directory: a damaged
                // or inconsistent one fails here, not in the constructor.
                manifestEntry = archive.GetEntry(ExportFormat.ManifestEntryName);
                payloadEntry = archive.GetEntry(ExportFormat.PayloadEntryName);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage, ex);
            }
            if (manifestEntry is null || payloadEntry is null)
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage);

            ExportManifest? manifest;
            try
            {
                using var manifestStream = manifestEntry.Open();
                manifest = JsonSerializer.Deserialize<ExportManifest>(
                    ReadExact(manifestStream, MaxManifestBytes, manifestEntry.Length), ExportJson.Options);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
            {
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage, ex);
            }

            if (manifest is null
                || !string.Equals(manifest.Format, ExportFormat.FormatIdentifier, StringComparison.Ordinal))
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage);

            // JSON null replaces the default sections: refuse it here
            // rather than fail later on a null reference.
            if (manifest.Kdf is null || manifest.Cipher is null || manifest.Payload is null)
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage);

            if (manifest.FormatVersion > ExportFormat.CurrentFormatVersion)
                throw new ImportFailedException(ImportFailureReason.UnsupportedVersion, NewerVersionMessage);

            if (!readPayload) return (manifest, []);

            byte[] ciphertext;
            try
            {
                using var payloadStream = payloadEntry.Open();
                ciphertext = ReadExact(payloadStream, MaxPayloadBytes, payloadEntry.Length);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage, ex);
            }

            return (manifest, ciphertext);
        }
    }

    // The entry count the end-of-central-directory record declares, the
    // larger of its two count fields; 0 when the record is missing (then
    // ZipArchive reports the archive as damaged). The record is the last
    // one found scanning back from the end, as ZipArchive finds it. A count
    // of 0xFFFF defers to a Zip64 record, which a two-entry export never
    // needs: it is above the limit as it stands.
    private static int DeclaredEntries(Stream archive)
    {
        var tailLength = (int)Math.Min(archive.Length, EndOfCentralDirectorySize + ushort.MaxValue);
        var tail = new byte[tailLength];
        var start = archive.Position;
        archive.Seek(-tailLength, SeekOrigin.End);
        archive.ReadExactly(tail);
        archive.Position = start;
        for (var at = tailLength - EndOfCentralDirectorySize; at >= 0; at--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(at)) != EndOfCentralDirectorySignature) continue;
            var onThisDisk = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(at + 8));
            var total = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(at + 10));
            return Math.Max(onThisDisk, total);
        }
        return 0;
    }

    // Reads at most `limit` bytes into one buffer; the first `length`
    // bytes hold the data. The declared sizes of a ZIP are not trusted, the
    // bytes read are counted; sizeHint (a declared size) only sizes the
    // buffer, so an honest entry is read with one allocation and no copy.
    private static byte[] ReadBounded(Stream source, int limit, long sizeHint, out int length)
    {
        var buffer = new byte[(int)Math.Clamp(sizeHint, 0, limit)];
        length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                // Full: grow only if the source has more.
                var next = source.ReadByte();
                if (next < 0) return buffer;
                if (length == limit)
                    throw new ImportFailedException(ImportFailureReason.Corrupt, TooLargeMessage);
                Array.Resize(ref buffer, (int)Math.Min(limit, Math.Max(81920L, buffer.Length * 2L)));
                buffer[length++] = (byte)next;
            }
            var read = source.Read(buffer, length, buffer.Length - length);
            if (read == 0) return buffer;
            length += read;
        }
    }

    private static byte[] ReadExact(Stream source, int limit, long sizeHint)
    {
        var buffer = ReadBounded(source, limit, sizeHint, out var length);
        if (length != buffer.Length) Array.Resize(ref buffer, length);
        return buffer;
    }

    // expectedLength: the size the format fixes (salt, nonce, tag); a
    // field of another size is damage, not a wrong passphrase, and AES-GCM
    // would refuse it with an unmapped ArgumentException.
    private static byte[] DecodeBase64(string? value, int? expectedLength = null)
    {
        if (value is null) throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage, ex);
        }
        if (expectedLength is { } length && bytes.Length != length)
            throw new ImportFailedException(ImportFailureReason.Corrupt, CorruptMessage);
        return bytes;
    }
}
