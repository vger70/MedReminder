using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Export;

namespace MedReminder.MobileSpikes.Spikes;

// S1 — AES-GCM on Android (ANALYSIS-B1-MOBILE-SYNC.md P12, §13 Phase 0):
// AesGcm.IsSupported, known answers through the in-box type and through
// the production IArchiveCipher, tag rejection, and an Argon2id known
// answer. Decrypting a desktop archive is S1b (S1DesktopArchive).
internal static class S1AesGcm
{
    private const string Spike = "S1";

    public static void Run(SpikeReport report, IArchiveCipher cipher)
    {
        var supported = AesGcm.IsSupported;
        report.Add(Spike, "AesGcm.IsSupported", supported ? Outcome.Pass : Outcome.Fail, supported.ToString());

        var plaintext = Encoding.UTF8.GetBytes(KnownAnswers.AesPlaintext);
        var aad = Encoding.UTF8.GetBytes(KnownAnswers.AesAssociatedData);
        var expectedCiphertext = Convert.FromHexString(KnownAnswers.AesCiphertextHex);
        var expectedTag = Convert.FromHexString(KnownAnswers.AesTagHex);

        SpikeRunner.Check(report, Spike, "AES-256-GCM known answer, in-box AesGcm", () =>
        {
            using var aes = new AesGcm(KnownAnswers.AesKey, ExportFormat.AesGcmTagSizeBytes);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[ExportFormat.AesGcmTagSizeBytes];
            aes.Encrypt(KnownAnswers.AesNonce, plaintext, ciphertext, tag, aad);
            var ok = ciphertext.AsSpan().SequenceEqual(expectedCiphertext) && tag.AsSpan().SequenceEqual(expectedTag);
            return (ok, ok ? "ciphertext and tag equal the reference" : "ciphertext or tag differs from the reference");
        });

        SpikeRunner.Check(report, Spike, "AES-256-GCM known answer, IArchiveCipher.Decrypt", () =>
        {
            var decrypted = cipher.Decrypt(KnownAnswers.AesKey, KnownAnswers.AesNonce, expectedTag, expectedCiphertext, aad);
            var ok = decrypted.AsSpan().SequenceEqual(plaintext);
            return (ok, ok ? "plaintext equals the reference" : "plaintext differs from the reference");
        });

        SpikeRunner.Check(report, Spike, "Tampered tag rejected", () =>
        {
            var tampered = (byte[])expectedTag.Clone();
            tampered[0] ^= 0x01;
            try
            {
                cipher.Decrypt(KnownAnswers.AesKey, KnownAnswers.AesNonce, tampered, expectedCiphertext, aad);
                return (false, "decryption succeeded with a wrong tag");
            }
            catch (CryptographicException ex)
            {
                return (true, $"{ex.GetType().Name} as on the desktop");
            }
        });

        SpikeRunner.Check(report, Spike, "IArchiveCipher round trip, 1 MiB, random nonce", () =>
        {
            var key = RandomNumberGenerator.GetBytes(ExportFormat.KeySizeBytes);
            var data = RandomNumberGenerator.GetBytes(1024 * 1024);
            var (nonce, tag, ciphertext) = cipher.Encrypt(key, data, aad);
            var ok = cipher.Decrypt(key, nonce, tag, ciphertext, aad).AsSpan().SequenceEqual(data);
            return (ok, ok ? "decrypted data equals the input" : "decrypted data differs");
        });

        SpikeRunner.Check(report, Spike, "Argon2id known answer (t=2, m=1024 KiB, p=2), IArchiveCipher.DeriveKey", () =>
        {
            var parameters = new Argon2Params { Iterations = 2, MemoryKiB = 1024, Parallelism = 2 };
            var key = cipher.DeriveKey(KnownAnswers.Passphrase.ToCharArray(), KnownAnswers.Salt, parameters);
            var ok = Convert.ToHexStringLower(key) == KnownAnswers.Argon2idSmallHex;
            return (ok, ok ? "key equals the reference" : "key differs from the reference");
        });
    }
}
