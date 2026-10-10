using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Tests.Support;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Export;

// ArchiveReader: the platform-neutral read half of the .mrz import
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.2 steps 1-6, §4.4).
public sealed class ArchiveReaderTests
{
    private const string Passphrase = "correct horse battery";

    private static ArchiveReader Reader() => new(new ArchiveCipher());

    private static ImportFailureReason FailureOf(Action act)
    {
        var ex = Assert.Throws<ImportFailedException>(act);
        return ex.Reason;
    }

    [Fact]
    public void Decrypts_a_valid_archive()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase);

        using var archive = Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray());

        archive.Manifest.Format.Should().Be(ExportFormat.FormatIdentifier);
        archive.Manifest.ProfileId.Should().Be("0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f");
        archive.Payload.Medicines.Should().ContainSingle(m => m.Name == "Sample");
        archive.Payload.StockMovements.Should().HaveCount(2);
        archive.Payload.NotificationSettings!.ToAddress.Should().Be("user@example.org");
    }

    [Fact]
    public void Reads_the_manifest_without_the_passphrase()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase);

        var manifest = Reader().ReadManifest(new MemoryStream(bytes));

        manifest.AppVersion.Should().Be("test");
        manifest.Kdf.Iterations.Should().Be(TestArchiveWriter.FastKdf.Iterations);
    }

    [Fact]
    public void Accepts_a_non_seekable_stream()
    {
        // The mobile file picker hands over a content stream.
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase);

        using var archive = Reader().Decrypt(new ForwardOnlyStream(bytes), Passphrase.ToCharArray());

        archive.Payload.Medicines.Should().HaveCount(1);
    }

    [Fact]
    public void Wrong_passphrase_is_reported_as_such()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase);

        FailureOf(() => Reader().Decrypt(new MemoryStream(bytes), "not the passphrase".ToCharArray()))
            .Should().Be(ImportFailureReason.WrongPassphrase);
    }

    [Fact]
    public void Tampered_ciphertext_is_indistinguishable_from_a_wrong_passphrase()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase,
            tamperCiphertext: c => { c[0] ^= 0xFF; return c; });

        FailureOf(() => Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray()))
            .Should().Be(ImportFailureReason.WrongPassphrase);
    }

    [Fact]
    public void Payload_hash_mismatch_is_corrupt()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase,
            tamperManifest: m => m.Payload.Sha256Base64 = Convert.ToBase64String(new byte[32]));

        FailureOf(() => Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray()))
            .Should().Be(ImportFailureReason.Corrupt);
    }

    [Fact]
    public void Newer_format_version_is_unsupported()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase,
            tamperManifest: m => m.FormatVersion = ExportFormat.CurrentFormatVersion + 1);

        FailureOf(() => Reader().ReadManifest(new MemoryStream(bytes)))
            .Should().Be(ImportFailureReason.UnsupportedVersion);
    }

    [Fact]
    public void Newer_schema_version_is_unsupported()
    {
        var payload = TestArchiveWriter.SamplePayload();
        payload.SchemaVersion = ExportFormat.CurrentSchemaVersion + 1;
        var bytes = TestArchiveWriter.Write(payload, Passphrase);

        FailureOf(() => Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray()))
            .Should().Be(ImportFailureReason.UnsupportedVersion);
    }

    [Fact]
    public void Foreign_format_identifier_is_corrupt()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase,
            tamperManifest: m => m.Format = "something-else");

        FailureOf(() => Reader().ReadManifest(new MemoryStream(bytes)))
            .Should().Be(ImportFailureReason.Corrupt);
    }

    [Fact]
    public void Missing_payload_entry_is_corrupt()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase, omitPayloadEntry: true);

        FailureOf(() => Reader().ReadManifest(new MemoryStream(bytes)))
            .Should().Be(ImportFailureReason.Corrupt);
    }

    [Fact]
    public void Not_a_zip_is_corrupt()
    {
        FailureOf(() => Reader().ReadManifest(new MemoryStream("plain text"u8.ToArray())))
            .Should().Be(ImportFailureReason.Corrupt);
    }

    [Theory]
    [InlineData(0, 1024, 1)]
    [InlineData(Argon2Params.MaxIterations + 1, 1024, 1)]
    [InlineData(1, Argon2Params.MaxMemoryKiB + 1, 1)]
    [InlineData(1, int.MaxValue, 1)]
    [InlineData(1, 4, 1)]
    [InlineData(1, 1024, 0)]
    [InlineData(1, 1024, Argon2Params.MaxParallelism + 1)]
    public void Kdf_parameters_outside_the_limits_are_corrupt(int iterations, int memoryKiB, int parallelism)
    {
        // A crafted manifest must not make the import derive a key with
        // unbounded memory or time: the reader refuses before deriving.
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase, tamperManifest: m =>
        {
            m.Kdf.Iterations = iterations;
            m.Kdf.MemoryKiB = memoryKiB;
            m.Kdf.Parallelism = parallelism;
        });

        FailureOf(() => Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray()))
            .Should().Be(ImportFailureReason.Corrupt);
    }

    [Fact]
    public void Oversized_payload_is_refused_while_decompressing()
    {
        // Zeros compress to a small entry that expands past the limit.
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase,
            tamperCiphertext: _ => new byte[ArchiveReader.MaxPayloadBytes + 1]);
        bytes.Length.Should().BeLessThan(ArchiveReader.MaxPayloadBytes / 100);

        var ex = Assert.Throws<ImportFailedException>(
            () => Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray()));

        ex.Reason.Should().Be(ImportFailureReason.Corrupt);
        ex.Message.Should().Contain("larger than any MedReminder export");
    }

    [Fact]
    public void Reading_the_manifest_leaves_the_payload_compressed()
    {
        // The preview does not inflate the payload: an oversized one does
        // not surface until the import decrypts it.
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase,
            tamperCiphertext: _ => new byte[ArchiveReader.MaxPayloadBytes + 1]);

        Reader().ReadManifest(new MemoryStream(bytes)).AppVersion.Should().Be("test");
    }

    [Theory]
    [InlineData("kdf")]
    [InlineData("cipher")]
    [InlineData("payload")]
    [InlineData("kdf.saltBase64")]
    [InlineData("cipher.nonceBase64")]
    [InlineData("cipher.tagBase64")]
    public void Null_manifest_fields_are_corrupt(string field)
    {
        var bytes = WithManifest(TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase), manifest =>
        {
            var parts = field.Split('.');
            var owner = parts.Length == 1 ? manifest : manifest[parts[0]]!.AsObject();
            owner[parts[^1]] = null;
        });

        FailureOf(() => Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray()))
            .Should().Be(ImportFailureReason.Corrupt);
    }

    [Theory]
    [InlineData("kdf", "saltBase64")]
    [InlineData("cipher", "nonceBase64")]
    [InlineData("cipher", "tagBase64")]
    public void Fields_of_the_wrong_size_are_corrupt(string section, string field)
    {
        var bytes = WithManifest(TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase),
            manifest => manifest[section]![field] = Convert.ToBase64String(new byte[5]));

        FailureOf(() => Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray()))
            .Should().Be(ImportFailureReason.Corrupt);
    }

    // Rewrites manifest.json as raw JSON, so a test can put what the
    // writer never does (a JSON null) in it.
    private static byte[] WithManifest(byte[] archive, Action<JsonObject> edit)
    {
        using var buffer = new MemoryStream();
        buffer.Write(archive);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(ExportFormat.ManifestEntryName)!;
            JsonObject manifest;
            using (var read = entry.Open()) manifest = JsonNode.Parse(read)!.AsObject();
            edit(manifest);
            entry.Delete();
            using var write = zip.CreateEntry(ExportFormat.ManifestEntryName).Open();
            write.Write(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        }
        return buffer.ToArray();
    }

    [Fact]
    public void Oversized_manifest_is_corrupt()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase,
            tamperManifest: m => m.AppVersion = new string('x', ArchiveReader.MaxManifestBytes));

        FailureOf(() => Reader().ReadManifest(new MemoryStream(bytes)))
            .Should().Be(ImportFailureReason.Corrupt);
    }

    [Fact]
    public void Oversized_archive_is_refused_before_reading()
    {
        FailureOf(() => Reader().ReadManifest(new LongStream(ArchiveReader.MaxArchiveBytes + 1L)))
            .Should().Be(ImportFailureReason.Corrupt);
    }

    [Fact]
    public void Oversized_non_seekable_archive_is_refused_while_reading()
    {
        var ex = Assert.Throws<ImportFailedException>(
            () => Reader().ReadManifest(new ForwardOnlyStream(new byte[ArchiveReader.MaxArchiveBytes + 1])));

        ex.Reason.Should().Be(ImportFailureReason.Corrupt);
        ex.Message.Should().Contain("larger than any MedReminder export");
    }

    [Fact]
    public void Cipher_refuses_kdf_parameters_outside_the_limits()
    {
        var act = () => new ArchiveCipher().DeriveKey("x".ToCharArray(), new byte[16],
            new Argon2Params { Iterations = 1, MemoryKiB = Argon2Params.MaxMemoryKiB * 2, Parallelism = 1 });

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Extra_entries_are_ignored()
    {
        // ZIP-slip mitigation (ANALYSIS-C3 §10): only the two known
        // entries are ever read.
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase,
            extraEntries: new Dictionary<string, string> { ["../evil.txt"] = "x" });

        using var archive = Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray());

        archive.Payload.Medicines.Should().HaveCount(1);
    }

    [Fact]
    public void Decrypts_an_opt_in_secret_only_on_request()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase, smtpPassword: "s3cret!");

        using var archive = Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray());

        archive.Payload.Shared.SmtpPasswordEncrypted.Should().NotBeNull();
        archive.DecryptSecretText(archive.Payload.Shared.SmtpPasswordEncrypted!).Should().Be("s3cret!");
    }

    [Fact]
    public void Tampered_secret_is_corrupt()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase, smtpPassword: "s3cret!");
        using var archive = Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray());
        var secret = archive.Payload.Shared.SmtpPasswordEncrypted!;
        secret.TagBase64 = Convert.ToBase64String(new byte[ExportFormat.AesGcmTagSizeBytes]);

        FailureOf(() => archive.DecryptSecret(secret)).Should().Be(ImportFailureReason.Corrupt);
    }

    [Fact]
    public void Disposed_archive_refuses_to_decrypt_secrets()
    {
        var bytes = TestArchiveWriter.Write(TestArchiveWriter.SamplePayload(), Passphrase, smtpPassword: "s3cret!");
        var archive = Reader().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray());
        var secret = archive.Payload.Shared.SmtpPasswordEncrypted!;

        archive.Dispose();

        Assert.Throws<ObjectDisposedException>(() => archive.DecryptSecret(secret));
    }

    // A stream that can only be read forward, like a content URI stream.
    // Seekable, reports a length, never read: the size check comes first.
    private sealed class LongStream(long length) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Read before the size check.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
