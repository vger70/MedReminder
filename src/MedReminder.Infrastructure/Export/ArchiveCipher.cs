using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using MedReminder.Application.Export;

namespace MedReminder.Infrastructure.Export;

// Concrete IArchiveCipher: Argon2id KDF (Konscious managed
// implementation) + AES-GCM (in-box) for the encrypted export archive
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §3.3, §13 step 3).
//
// Security posture (CLAUDE.md §9): the passphrase, the derived key and
// the plaintext are never logged. Transient key material derived here
// is zeroed; the returned key buffer is owned by the caller, which
// zeroes it after use (ExportService / ImportService do so in a
// finally block).
internal sealed class ArchiveCipher : IArchiveCipher
{
    public byte[] DeriveKey(char[] passphrase, byte[] salt, Argon2Params parameters)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(parameters);

        // Encode the passphrase as UTF-8 bytes for the KDF. Zero the
        // intermediate byte buffer after derivation so the secret does
        // not linger on the managed heap longer than necessary.
        var passwordBytes = Encoding.UTF8.GetBytes(passphrase);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                Iterations = parameters.Iterations,
                MemorySize = parameters.MemoryKiB,
                DegreeOfParallelism = parameters.Parallelism,
            };
            return argon2.GetBytes(ExportFormat.KeySizeBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public (byte[] Nonce, byte[] Tag, byte[] Ciphertext) Encrypt(byte[] key, byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(ExportFormat.AesGcmNonceSizeBytes);
        var tag = new byte[ExportFormat.AesGcmTagSizeBytes];
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(key, ExportFormat.AesGcmTagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return (nonce, tag, ciphertext);
    }

    public byte[] Decrypt(byte[] key, byte[] nonce, byte[] tag, byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(ciphertext);

        var plaintext = new byte[ciphertext.Length];

        // Throws CryptographicException on a tag mismatch (wrong key or
        // tampered data); the caller maps that to the wrong-passphrase
        // surface (§4.4).
        using var aes = new AesGcm(key, ExportFormat.AesGcmTagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
