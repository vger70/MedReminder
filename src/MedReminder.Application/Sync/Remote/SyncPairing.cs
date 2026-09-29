using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;

namespace MedReminder.Application.Sync.Remote;

// The text of a pairing QR code (B.1 Phase 4c, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §6.1, docs/SYNC-FORMAT.md §4.4):
//
//   mrpair1.<groupId>.<deviceId>.<provider>.<secret>
//
// groupId and deviceId in N format; provider "folder", "OneDrive" or
// "GoogleDrive"; secret: 32 random bytes, base64url without padding.
// The code does not hold the group key: it names the pairing file of the
// device that shows it and holds the key that opens that file. When the
// offer ends the file is deleted, and a photo of the code opens nothing.
public sealed record SyncPairingCode(Guid GroupId, Guid DeviceId, CloudProvider? Provider, byte[] Secret)
{
    public const string Prefix = "mrpair1";
    public const int SecretSize = 32;
    private const string FolderName = "folder";

    // The text to encode in the QR code. Secret: never logged.
    public string Text => Format(Prefix, GroupId, DeviceId, Provider, Secret);

    // Leaves the secret out of any accidental log line.
    public override string ToString() => $"Pairing code of device {DeviceId:N}";

    // Accepts the text with spaces or line breaks added by copying.
    public static bool TryParse(string? text, out SyncPairingCode? code)
    {
        code = null;
        if (!TryParse(text, Prefix, out var groupId, out var deviceId, out var provider, out var secret)) return false;
        code = new SyncPairingCode(groupId, deviceId, provider, secret);
        return true;
    }

    internal static string Format(string prefix, Guid groupId, Guid deviceId, CloudProvider? provider, byte[] secret)
        => string.Join('.', prefix, groupId.ToString("N"), deviceId.ToString("N"),
            provider?.ToString() ?? FolderName, Base64Url.EncodeToString(secret));

    // The five parts shared by mrpair1 and mrpair2 (household step H3c).
    internal static bool TryParse(string? text, string prefix, out Guid groupId, out Guid deviceId,
        out CloudProvider? provider, out byte[] secret)
    {
        groupId = deviceId = Guid.Empty;
        provider = null;
        secret = [];
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = string.Concat(text.Where(c => !char.IsWhiteSpace(c))).Split('.');
        if (parts.Length != 5 || parts[0] != prefix
            || !Guid.TryParseExact(parts[1], "N", out groupId)
            || !Guid.TryParseExact(parts[2], "N", out deviceId))
        {
            return false;
        }

        switch (parts[3])
        {
            case FolderName: provider = null; break;
            case nameof(CloudProvider.OneDrive): provider = CloudProvider.OneDrive; break;
            case nameof(CloudProvider.GoogleDrive): provider = CloudProvider.GoogleDrive; break;
            default: return false;
        }

        var decoded = new byte[SecretSize];
        if (!Base64Url.TryDecodeFromChars(parts[4], decoded, out var written) || written != SecretSize) return false;
        secret = decoded;
        return true;
    }
}

// The pairing offer is over: its file was removed when the showing device
// closed the dialog, or its 10 minutes passed.
public sealed class SyncPairingExpiredException : Exception
{
    public SyncPairingExpiredException()
        : base("The pairing code has expired; show a new one on the paired device.")
    {
    }
}

// <groupId>/pairing/<deviceId>.mrp: UTF-8 JSON, written by the device
// that shows the code, removed when the offer ends. The group key of the
// offer, its key version and the expiry are encrypted (AES-256-GCM) with
// the secret of the code; associated data
// "MedReminder.Sync.Pairing|<groupId N>|<deviceId N>". Nothing in the
// cleartext part is personal.
public sealed record SyncPairingFile(int FormatVersion, Guid GroupId, Guid DeviceId, byte[] Nonce, byte[] Tag, byte[] Ciphertext)
{
    private sealed record Content(int KeyVersion, byte[] Key, DateTimeOffset ExpiresAt);

    public static SyncPairingFile Seal(IArchiveCipher cipher, SyncPairingCode code, int keyVersion, byte[] key,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(code);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new Content(keyVersion, key, expiresAt), SyncFileCodec.Json);
        try
        {
            var (nonce, tag, ciphertext) = cipher.Encrypt(code.Secret, plaintext, Aad(code.GroupId, code.DeviceId));
            return new SyncPairingFile(SyncFileCodec.FormatVersion, code.GroupId, code.DeviceId, nonce, tag, ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    // The key version and the group key. Throws CryptographicException
    // when the code does not open the file (another offer replaced it),
    // SyncPairingExpiredException when the offer is over.
    public (int KeyVersion, byte[] Key) Open(IArchiveCipher cipher, SyncPairingCode code, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(code);
        if (FormatVersion > SyncFileCodec.FormatVersion)
            throw new NotSupportedException($"Pairing format {FormatVersion} is not supported.");
        if (GroupId != code.GroupId || DeviceId != code.DeviceId)
            throw new InvalidDataException("The pairing file does not match the code.");

        var plaintext = cipher.Decrypt(code.Secret, Nonce, Tag, Ciphertext, Aad(GroupId, DeviceId));
        try
        {
            var content = JsonSerializer.Deserialize<Content>(plaintext, SyncFileCodec.Json)
                ?? throw new InvalidDataException("Empty pairing file.");
            if (now > content.ExpiresAt)
            {
                CryptographicOperations.ZeroMemory(content.Key);
                throw new SyncPairingExpiredException();
            }
            return (content.KeyVersion, content.Key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, SyncFileCodec.Json);

    public static SyncPairingFile Parse(byte[] content)
        => JsonSerializer.Deserialize<SyncPairingFile>(content, SyncFileCodec.Json)
            ?? throw new InvalidDataException("Empty pairing file.");

    private static byte[] Aad(Guid groupId, Guid deviceId)
        => Encoding.UTF8.GetBytes($"MedReminder.Sync.Pairing|{groupId:N}|{deviceId:N}");
}
