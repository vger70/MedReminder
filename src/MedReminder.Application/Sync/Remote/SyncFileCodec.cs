using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedReminder.Application.Export;

namespace MedReminder.Application.Sync.Remote;

public static class SyncFileKind
{
    public const string Segment = "segment";
    public const string Checkpoint = "checkpoint";
    public const string Genesis = "genesis";
    public const string Device = "device";
}

// Cleartext header of an encrypted sync file, bound to the ciphertext as
// AES-GCM associated data (§5.2): renaming a file, moving it to another
// device folder or changing a number in it fails authentication. Ids and
// counters only (R4). Vector is set on checkpoints, so a device can pick
// one without decrypting it.
public sealed record SyncFileHeader(
    string Kind,
    int FormatVersion,
    Guid GroupId,
    int Generation,
    Guid DeviceId,
    int Seq,
    int KeyVersion,
    int ContentVersion,
    IReadOnlyDictionary<Guid, int>? Vector = null);

// Encrypted file envelope (docs/SYNC-FORMAT.md §3):
//
//   "MRS1" | uint32 LE header length | header JSON (UTF-8)
//          | 12-byte nonce | 16-byte tag | AES-256-GCM ciphertext
//
// The plaintext is gzip-compressed content. A random nonce per file, as
// in the export archive.
public static class SyncFileCodec
{
    public const int FormatVersion = 1;

    private static readonly byte[] Magic = "MRS1"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        WriteIndented = false,
    };

    public static byte[] Seal(IArchiveCipher cipher, byte[] key, SyncFileHeader header, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(header);
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, Json);
        var (nonce, tag, ciphertext) = cipher.Encrypt(key, Compress(content), headerBytes);

        var file = new byte[Magic.Length + 4 + headerBytes.Length + NonceSize + TagSize + ciphertext.Length];
        var span = file.AsSpan();
        Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)headerBytes.Length);
        headerBytes.CopyTo(span[8..]);
        var at = 8 + headerBytes.Length;
        nonce.CopyTo(span[at..]);
        tag.CopyTo(span[(at + NonceSize)..]);
        ciphertext.CopyTo(span[(at + NonceSize + TagSize)..]);
        return file;
    }

    // The header, readable without the key. Authenticated only by Open.
    public static SyncFileHeader ReadHeader(byte[] file)
        => JsonSerializer.Deserialize<SyncFileHeader>(HeaderBytes(file), Json)
            ?? throw new InvalidDataException("Empty sync file header.");

    // Throws CryptographicException when the key is wrong or anything in
    // the file was altered.
    public static (SyncFileHeader Header, byte[] Content) Open(IArchiveCipher cipher, byte[] key, byte[] file)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        var headerBytes = HeaderBytes(file);
        var at = 8 + headerBytes.Length;
        if (file.Length < at + NonceSize + TagSize) throw new InvalidDataException("Truncated sync file.");
        var plaintext = cipher.Decrypt(
            key,
            file[at..(at + NonceSize)],
            file[(at + NonceSize)..(at + NonceSize + TagSize)],
            file[(at + NonceSize + TagSize)..],
            headerBytes);
        var header = JsonSerializer.Deserialize<SyncFileHeader>(headerBytes, Json)
            ?? throw new InvalidDataException("Empty sync file header.");
        if (header.FormatVersion > FormatVersion)
            throw new NotSupportedException($"Sync file format {header.FormatVersion} is not supported.");
        return (header, Decompress(plaintext));
    }

    private static byte[] HeaderBytes(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length < 8 || !file.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException("Not a MedReminder sync file.");
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4));
        if (length <= 0 || 8 + length > file.Length) throw new InvalidDataException("Corrupt sync file header.");
        return file[8..(8 + length)];
    }

    private static byte[] Compress(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(content);
        }
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] content)
    {
        using var input = new GZipStream(new MemoryStream(content), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
