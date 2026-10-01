using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Export;

namespace MedReminder.Application.Household;

// Wraps a secret for the holder of a private key (household step H3b;
// docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §4.4): ECDH on P-256
// with a fresh ephemeral key, HKDF-SHA256 over the shared secret, AES-256-
// GCM through IArchiveCipher. The purpose string names what is wrapped and
// for whom; it is the HKDF info and the AES-GCM associated data, so a
// wrapped key copied to another grant or escrow does not open.
//
// Format: "1.<ephemeral public key>.<nonce>.<tag>.<ciphertext>", each part
// base64url without padding (no ':' so it fits a household register).
// Keys: SubjectPublicKeyInfo (public) and PKCS#8 (private), base64.
public static class HouseholdKeyWrap
{
    private const string Version = "1";

    public static (string PrivateKey, string PublicKey) CreateKeyPair()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdh.ExportPkcs8PrivateKey()),
            Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo()));
    }

    // The public key of a private key, to check or republish it.
    public static string PublicKeyOf(string privateKey)
    {
        using var ecdh = ImportPrivate(privateKey);
        return Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo());
    }

    public static string Wrap(IArchiveCipher cipher, string recipientPublicKey, byte[] secret, string purpose)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(secret);
        using var recipient = ECDiffieHellman.Create();
        recipient.ImportSubjectPublicKeyInfo(Convert.FromBase64String(recipientPublicKey), out _);
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var ephemeralPublic = ephemeral.ExportSubjectPublicKeyInfo();
        var key = DeriveKey(ephemeral, recipient.PublicKey, ephemeralPublic, purpose);
        try
        {
            var (nonce, tag, ciphertext) = cipher.Encrypt(key, secret, Encoding.UTF8.GetBytes(purpose));
            return string.Join('.', Version, Base64Url(ephemeralPublic), Base64Url(nonce), Base64Url(tag), Base64Url(ciphertext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    // CryptographicException when the private key or the purpose does not
    // match, or the wrapped value was altered.
    public static byte[] Unwrap(IArchiveCipher cipher, string privateKey, string wrapped, string purpose)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        var parts = wrapped?.Split('.') ?? [];
        if (parts.Length != 5 || parts[0] != Version) throw new CryptographicException("Unknown wrapped key format.");
        var ephemeralPublic = FromBase64Url(parts[1]);
        using var own = ImportPrivate(privateKey);
        using var ephemeral = ECDiffieHellman.Create();
        ephemeral.ImportSubjectPublicKeyInfo(ephemeralPublic, out _);
        var key = DeriveKey(own, ephemeral.PublicKey, ephemeralPublic, purpose);
        try
        {
            return cipher.Decrypt(key, FromBase64Url(parts[2]), FromBase64Url(parts[3]), FromBase64Url(parts[4]),
                Encoding.UTF8.GetBytes(purpose));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static string GrantPurpose(Guid groupId, int keyVersion, Guid deviceId)
        => $"MedReminder.Household.Grant|{groupId:N}|{keyVersion}|{deviceId:N}";

    public static string EscrowPurpose(Guid groupId, int keyVersion)
        => $"MedReminder.Household.Escrow|{groupId:N}|{keyVersion}";

    private static ECDiffieHellman ImportPrivate(string privateKey)
    {
        var ecdh = ECDiffieHellman.Create();
        ecdh.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        return ecdh;
    }

    private static byte[] DeriveKey(ECDiffieHellman own, ECDiffieHellmanPublicKey other, byte[] ephemeralPublic, string purpose)
    {
        var shared = own.DeriveRawSecretAgreement(other);
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, ephemeralPublic, Encoding.UTF8.GetBytes(purpose));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
