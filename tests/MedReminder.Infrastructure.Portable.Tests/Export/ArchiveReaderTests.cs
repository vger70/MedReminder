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
