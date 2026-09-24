using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Export;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Export;

// Covers the Argon2id + AES-GCM adapter (C.3,
// docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §8.2, prompt Steps 2/3).
// Uses fast Argon2id parameters so the KDF assertions stay quick; the
// production defaults are exercised by the round-trip tests.
public class ArchiveCipherTests
{
    // Deliberately cheap so the deterministic-KDF assertions run fast.
    private static readonly Argon2Params FastParams = new()
    {
        Iterations = 1,
        MemoryKiB = 1024,
        Parallelism = 1,
    };

    private static readonly IArchiveCipher Cipher = new ArchiveCipher();

    [Fact]
    public void DeriveKey_is_deterministic_for_same_passphrase_and_salt()
    {
        var salt = RandomNumberGenerator.GetBytes(ExportFormat.SaltSizeBytes);

        var key1 = Cipher.DeriveKey("correct horse battery staple".ToCharArray(), salt, FastParams);
        var key2 = Cipher.DeriveKey("correct horse battery staple".ToCharArray(), salt, FastParams);

        key1.Should().HaveCount(ExportFormat.KeySizeBytes);
        key1.Should().Equal(key2);
    }

    [Fact]
    public void DeriveKey_produces_distinct_keys_for_distinct_salts()
    {
        var passphrase = "correct horse battery staple".ToCharArray();
        var saltA = new byte[ExportFormat.SaltSizeBytes]; // all zeros
        var saltB = new byte[ExportFormat.SaltSizeBytes];
        saltB[0] = 1;

        var keyA = Cipher.DeriveKey(passphrase, saltA, FastParams);
        var keyB = Cipher.DeriveKey(passphrase, saltB, FastParams);

        keyA.Should().NotEqual(keyB);
    }

    [Fact]
    public void DeriveKey_produces_distinct_keys_for_distinct_passphrases()
    {
        var salt = RandomNumberGenerator.GetBytes(ExportFormat.SaltSizeBytes);

        var keyA = Cipher.DeriveKey("passphrase one".ToCharArray(), salt, FastParams);
        var keyB = Cipher.DeriveKey("passphrase two".ToCharArray(), salt, FastParams);

        keyA.Should().NotEqual(keyB);
    }

    [Fact]
    public void Encrypt_then_Decrypt_round_trips_the_plaintext()
    {
        var key = RandomNumberGenerator.GetBytes(ExportFormat.KeySizeBytes);
        var plaintext = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog");

        var (nonce, tag, ciphertext) = Cipher.Encrypt(key, plaintext);

        nonce.Should().HaveCount(ExportFormat.AesGcmNonceSizeBytes);
        tag.Should().HaveCount(ExportFormat.AesGcmTagSizeBytes);
        ciphertext.Should().NotEqual(plaintext);

        var decrypted = Cipher.Decrypt(key, nonce, tag, ciphertext);
        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void Encrypt_uses_a_fresh_nonce_each_call()
    {
        var key = RandomNumberGenerator.GetBytes(ExportFormat.KeySizeBytes);
        var plaintext = Encoding.UTF8.GetBytes("same input twice");

        var first = Cipher.Encrypt(key, plaintext);
        var second = Cipher.Encrypt(key, plaintext);

        first.Nonce.Should().NotEqual(second.Nonce);
        first.Ciphertext.Should().NotEqual(second.Ciphertext);
    }

    [Fact]
    public void Decrypt_with_wrong_key_throws()
    {
        var key = RandomNumberGenerator.GetBytes(ExportFormat.KeySizeBytes);
        var wrongKey = RandomNumberGenerator.GetBytes(ExportFormat.KeySizeBytes);
        var plaintext = Encoding.UTF8.GetBytes("secret");

        var (nonce, tag, ciphertext) = Cipher.Encrypt(key, plaintext);

        var act = () => Cipher.Decrypt(wrongKey, nonce, tag, ciphertext);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_with_tampered_ciphertext_throws()
    {
        var key = RandomNumberGenerator.GetBytes(ExportFormat.KeySizeBytes);
        var plaintext = Encoding.UTF8.GetBytes("secret payload");

        var (nonce, tag, ciphertext) = Cipher.Encrypt(key, plaintext);
        ciphertext[0] ^= 0xFF; // flip one byte

        var act = () => Cipher.Decrypt(key, nonce, tag, ciphertext);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_with_tampered_tag_throws()
    {
        var key = RandomNumberGenerator.GetBytes(ExportFormat.KeySizeBytes);
        var plaintext = Encoding.UTF8.GetBytes("secret payload");

        var (nonce, tag, ciphertext) = Cipher.Encrypt(key, plaintext);
        tag[0] ^= 0xFF; // flip one tag byte

        var act = () => Cipher.Decrypt(key, nonce, tag, ciphertext);
        act.Should().Throw<CryptographicException>();
    }
}
