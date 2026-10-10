using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Export;
using MedReminder.Application.Sync.Remote;
using MedReminder.Infrastructure.Export;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Sync;

// The encrypted file envelope and the key wrap (docs/SYNC-FORMAT.md §3,
// §4) with the real AES-GCM and Argon2id adapter.
public class SyncFileFormatTests
{
    internal static readonly Argon2Params FastKdf = new() { Iterations = 1, MemoryKiB = 1024, Parallelism = 1 };

    private readonly ArchiveCipher _cipher = new();
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    private static SyncFileHeader Header(int seq = 1)
        => new(SyncFileKind.Segment, 1, Guid.NewGuid(), 1, Guid.NewGuid(), seq, 1, 1);

    [Fact]
    public void Sealed_content_round_trips_and_the_header_is_readable_without_the_key()
    {
        var header = Header();
        var content = Encoding.UTF8.GetBytes("operations");

        var file = _cipher.SealWith(_key, header, content);

        SyncFileCodec.ReadHeader(file).Should().BeEquivalentTo(header);
        var (opened, bytes) = SyncFileCodec.Open(_cipher, _key, file);
        opened.Should().BeEquivalentTo(header);
        bytes.Should().Equal(content);
    }

    [Fact]
    public void A_changed_header_fails_authentication()
    {
        var file = _cipher.SealWith(_key, Header(seq: 1), [1, 2, 3]);
        // Same length: seq 1 -> 2 inside the cleartext JSON.
        var text = Encoding.UTF8.GetString(file);
        var index = text.IndexOf("\"seq\":1", StringComparison.Ordinal);
        file[index + 6] = (byte)'2';

        FluentActions.Invoking(() => SyncFileCodec.Open(_cipher, _key, file))
            .Should().Throw<CryptographicException>();
    }

    [Fact]
    public void A_wrong_key_or_a_flipped_byte_fails()
    {
        var file = _cipher.SealWith(_key, Header(), [1, 2, 3]);

        FluentActions.Invoking(() => SyncFileCodec.Open(_cipher, RandomNumberGenerator.GetBytes(32), file))
            .Should().Throw<CryptographicException>();
        file[^1] ^= 0xFF;
        FluentActions.Invoking(() => SyncFileCodec.Open(_cipher, _key, file))
            .Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Key_wrap_opens_only_with_the_passphrase()
    {
        var groupId = Guid.NewGuid();
        var wrap = SyncKeyWrap.Wrap(_cipher, groupId, 1, _key, "correct horse".ToCharArray(), FastKdf);
        var parsed = SyncKeyWrap.Parse(wrap.ToBytes());

        parsed.Unwrap(_cipher, "correct horse".ToCharArray()).Should().Equal(_key);
        FluentActions.Invoking(() => parsed.Unwrap(_cipher, "wrong".ToCharArray()))
            .Should().Throw<CryptographicException>();
        FluentActions.Invoking(() => (parsed with { KeyVersion = 2 }).Unwrap(_cipher, "correct horse".ToCharArray()))
            .Should().Throw<CryptographicException>("the group id and key version are associated data");
    }

    [Fact]
    public void Key_wrap_with_a_crafted_kdf_cost_is_refused_before_deriving()
    {
        var wrap = SyncKeyWrap.Wrap(_cipher, Guid.NewGuid(), 1, _key, "correct horse".ToCharArray(), FastKdf)
            with { MemoryKiB = Argon2Params.MaxMemoryKiB * 4 };

        FluentActions.Invoking(() => wrap.Unwrap(_cipher, "correct horse".ToCharArray()))
            .Should().Throw<InvalidDataException>();
    }
}

internal static class CipherExtensions
{
    public static byte[] SealWith(this IArchiveCipher cipher, byte[] key, SyncFileHeader header, byte[] content)
        => SyncFileCodec.Seal(cipher, key, header, content);
}
