using FluentAssertions;
using MedReminder.Application.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Backup;

// Contract every IArchiveStorage implementation must honour
// (docs/analysis/ANALYSIS-C3PP-CLOUD-PROVIDERS.md §10.1). Each backend
// derives a concrete class that supplies its own storage instances, so
// a new backend cannot drift from the semantics the backup host and
// the retention pruning rely on.
public abstract class ArchiveStorageContractTests
{
    // Storage over an available, initially empty target.
    protected abstract IArchiveStorage CreateStorage();

    // Storage whose target does not exist.
    protected abstract IArchiveStorage CreateUnavailableStorage();

    [Fact]
    public async Task Upload_list_download_delete_round_trips()
    {
        var storage = CreateStorage();
        var payload = RandomBytes(1024);

        var id = await storage.UploadAsync(
            new MemoryStream(payload), ArchiveName(0), CancellationToken.None);

        var listed = await storage.ListAsync(CancellationToken.None);
        listed.Should().ContainSingle();
        listed[0].Id.Should().Be(id);
        listed[0].Name.Should().Be(ArchiveName(0));
        listed[0].SizeBytes.Should().Be(payload.Length);

        await using (var downloaded = await storage.DownloadAsync(id, CancellationToken.None))
        {
            var buffer = new MemoryStream();
            await downloaded.CopyToAsync(buffer);
            buffer.ToArray().Should().Equal(payload);
        }

        await storage.DeleteAsync(id, CancellationToken.None);

        (await storage.ListAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_disposes_the_stream()
    {
        var stream = new MemoryStream(RandomBytes(16));

        await CreateStorage().UploadAsync(stream, ArchiveName(0), CancellationToken.None);

        stream.CanRead.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_upload_leaves_no_archive()
    {
        var storage = CreateStorage();

        var act = () => storage.UploadAsync(
            new FailingStream(bytesBeforeFailure: 512), ArchiveName(0), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        (await storage.ListAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_to_an_unavailable_target_throws_DirectoryNotFoundException()
    {
        var storage = CreateUnavailableStorage();

        var act = () => storage.UploadAsync(
            new MemoryStream(RandomBytes(16)), ArchiveName(0), CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
        (await storage.ListAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_the_oldest_of_n_plus_one_archives_leaves_n()
    {
        const int retained = 3;
        var storage = CreateStorage();
        for (var i = 0; i <= retained; i++)
        {
            await storage.UploadAsync(
                new MemoryStream(RandomBytes(64)), ArchiveName(i), CancellationToken.None);
        }

        var listed = await storage.ListAsync(CancellationToken.None);
        listed.Should().HaveCount(retained + 1);
        listed.Select(a => a.CreatedAtUtc).Should().BeInDescendingOrder();

        var oldest = listed[^1];
        await storage.DeleteAsync(oldest.Id, CancellationToken.None);

        var remaining = await storage.ListAsync(CancellationToken.None);
        remaining.Should().HaveCount(retained);
        remaining.Should().NotContain(a => a.Id == oldest.Id);
    }

    [Fact]
    public async Task Deleting_an_archive_that_no_longer_exists_is_a_no_op()
    {
        var storage = CreateStorage();
        var id = await storage.UploadAsync(
            new MemoryStream(RandomBytes(16)), ArchiveName(0), CancellationToken.None);
        await storage.DeleteAsync(id, CancellationToken.None);

        var act = () => storage.DeleteAsync(id, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    protected static string ArchiveName(int index) =>
        $"medreminder-{new string('a', 32)}-20260101-{120000 + index:D6}.mrz";

    protected static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    // Yields bytesBeforeFailure bytes, then fails like a broken source
    // (disk error, cancelled export) in the middle of the copy.
    protected sealed class FailingStream(int bytesBeforeFailure) : Stream
    {
        private int _remaining = bytesBeforeFailure;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) throw new IOException("Simulated read failure.");
            var read = Math.Min(count, _remaining);
            Array.Fill(buffer, (byte)0x5A, offset, read);
            _remaining -= read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
