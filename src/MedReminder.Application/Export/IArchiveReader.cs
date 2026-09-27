using System.Security.Cryptography;
using System.Text;

namespace MedReminder.Application.Export;

// Reads an encrypted .mrz archive without applying it anywhere
// (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2 of revision 1, §13
// Phase 1). The platform-neutral implementation lives in
// MedReminder.Infrastructure.Portable (ArchiveReader); the Windows
// ImportService and the future mobile host apply the result in their
// own way. Every failure is an ImportFailedException with the same
// reasons as IImportService (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md
// §4.4).
public interface IArchiveReader
{
    // Parses and version-checks manifest.json without decrypting.
    ExportManifest ReadManifest(Stream archive);

    // Validates the manifest, derives the key, decrypts payload.enc,
    // checks the SHA-256 and the schema version. The caller owns
    // passphrase and should zero it after the call, and must dispose the
    // result to zero the derived key.
    DecryptedArchive Decrypt(Stream archive, char[] passphrase);
}

// A decrypted archive. Holds the derived key only so that opt-in
// secrets carried in the payload (the SMTP password) can be decrypted
// on explicit request; nothing is decrypted beyond payload.json unless
// the caller asks. Never log the key or any plaintext (CLAUDE.md §7).
public sealed class DecryptedArchive : IDisposable
{
    private readonly byte[] _key;
    private readonly IArchiveCipher _cipher;
    private bool _disposed;

    public DecryptedArchive(ExportManifest manifest, ExportPayload payload, byte[] key, IArchiveCipher cipher)
    {
        Manifest = manifest;
        Payload = payload;
        _key = key;
        _cipher = cipher;
    }

    public ExportManifest Manifest { get; }

    public ExportPayload Payload { get; }

    // Decrypts a secret re-encrypted with the archive key (§3.4). The
    // caller owns the returned buffer and must zero it after use. The
    // archive key already decrypted the payload, so a failure here means
    // a tampered secret block: reported as Corrupt.
    public byte[] DecryptSecret(ExportedProtectedSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var nonce = DecodeBase64(secret.NonceBase64);
        var tag = DecodeBase64(secret.TagBase64);
        var ciphertext = DecodeBase64(secret.CiphertextBase64);
        try
        {
            return _cipher.Decrypt(_key, nonce, tag, ciphertext);
        }
        catch (CryptographicException ex)
        {
            throw Corrupt(ex);
        }
    }

    // Convenience for text secrets. The returned string cannot be zeroed;
    // prefer DecryptSecret when the caller can work on bytes.
    public string DecryptSecretText(ExportedProtectedSecret secret)
    {
        var bytes = DecryptSecret(secret);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }

    private static byte[] DecodeBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw Corrupt(ex);
        }
    }

    private static ImportFailedException Corrupt(Exception inner)
        => new(ImportFailureReason.Corrupt, "The export file is damaged and cannot be imported.", inner);
}
