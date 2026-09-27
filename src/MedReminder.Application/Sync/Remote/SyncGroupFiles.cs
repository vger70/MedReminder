using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedReminder.Application.Export;

namespace MedReminder.Application.Sync.Remote;

// group.json: cleartext, written once when the group is created.
public sealed record SyncGroupFile(string Format, int FormatVersion, Guid GroupId)
{
    public const string FormatName = "MedReminder.Sync";

    public static SyncGroupFile Create(Guid groupId) => new(FormatName, SyncFileCodec.FormatVersion, groupId);

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, SyncFileCodec.Json);

    public static SyncGroupFile Parse(byte[] content)
    {
        var file = JsonSerializer.Deserialize<SyncGroupFile>(content, SyncFileCodec.Json)
            ?? throw new InvalidDataException("Empty group.json.");
        if (file.Format != FormatName) throw new InvalidDataException("Not a MedReminder sync group.");
        if (file.FormatVersion > SyncFileCodec.FormatVersion)
            throw new NotSupportedException($"Sync format {file.FormatVersion} is not supported.");
        return file;
    }
}

// key.<v>.wrap: the group key encrypted with a key derived from the sync
// passphrase (Argon2id, §5.4; the sync passphrase is not the backup
// one, D10). The group id and key version are associated data.
public sealed record SyncKeyWrap(
    int FormatVersion,
    Guid GroupId,
    int KeyVersion,
    int Iterations,
    int MemoryKiB,
    int Parallelism,
    byte[] Salt,
    byte[] Nonce,
    byte[] Tag,
    byte[] Ciphertext)
{
    public const int KeySize = 32;
    private const int SaltSize = 16;

    public static SyncKeyWrap Wrap(IArchiveCipher cipher, Guid groupId, int keyVersion, byte[] groupKey,
        char[] passphrase, Argon2Params parameters)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(parameters);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var wrappingKey = cipher.DeriveKey(passphrase, salt, parameters);
        try
        {
            var (nonce, tag, ciphertext) = cipher.Encrypt(wrappingKey, groupKey, Aad(groupId, keyVersion));
            return new SyncKeyWrap(SyncFileCodec.FormatVersion, groupId, keyVersion,
                parameters.Iterations, parameters.MemoryKiB, parameters.Parallelism, salt, nonce, tag, ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    // Throws CryptographicException for a wrong passphrase.
    public byte[] Unwrap(IArchiveCipher cipher, char[] passphrase)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        var wrappingKey = cipher.DeriveKey(passphrase, Salt,
            new Argon2Params { Iterations = Iterations, MemoryKiB = MemoryKiB, Parallelism = Parallelism });
        try
        {
            return cipher.Decrypt(wrappingKey, Nonce, Tag, Ciphertext, Aad(GroupId, KeyVersion));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, SyncFileCodec.Json);

    public static SyncKeyWrap Parse(byte[] content)
        => JsonSerializer.Deserialize<SyncKeyWrap>(content, SyncFileCodec.Json)
            ?? throw new InvalidDataException("Empty key wrap.");

    private static byte[] Aad(Guid groupId, int keyVersion)
        => Encoding.UTF8.GetBytes($"MedReminder.Sync.Key|{groupId:N}|{keyVersion}");
}
