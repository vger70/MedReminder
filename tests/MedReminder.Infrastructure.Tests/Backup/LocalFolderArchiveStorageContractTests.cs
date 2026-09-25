using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Backup;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Backup;

// Runs the IArchiveStorage contract against LocalFolderArchiveStorage,
// plus the local-folder specifics: temp-then-move discipline, temp-file
// cleanup, extension filtering, live folder reconfiguration and the
// last-write timestamp retention relies on.
public sealed class LocalFolderArchiveStorageContractTests : ArchiveStorageContractTests, IDisposable
{
    private readonly string _root;
    private readonly string _tempDirectory;
    private string _folder;

    public LocalFolderArchiveStorageContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mr-archive-storage-" + Guid.NewGuid().ToString("N"));
        _folder = Path.Combine(_root, "cloud");
        _tempDirectory = Path.Combine(_root, "temp");
        Directory.CreateDirectory(_folder);
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    protected override IArchiveStorage CreateStorage() => CreateLocalStorage();

    protected override IArchiveStorage CreateUnavailableStorage() =>
        new LocalFolderArchiveStorage(
            () => Path.Combine(_root, "missing"),
            _tempDirectory,
            NullLogger<LocalFolderArchiveStorage>.Instance);

    [Fact]
    public async Task Upload_id_is_the_file_in_the_configured_folder()
    {
        var id = await CreateLocalStorage().UploadAsync(
            new MemoryStream(RandomBytes(32)), ArchiveName(0), CancellationToken.None);

        id.Should().Be(Path.Combine(_folder, ArchiveName(0)));
        File.Exists(id).Should().BeTrue();
    }

    [Fact]
    public async Task Failed_upload_leaves_no_file_in_the_folder_and_no_temp_file()
    {
        var act = () => CreateLocalStorage().UploadAsync(
            new FailingStream(bytesBeforeFailure: 512), ArchiveName(0), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        Directory.EnumerateFileSystemEntries(_folder).Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(_tempDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task Move_failure_after_the_temp_write_keeps_the_existing_file_and_cleans_up()
    {
        // The name is already taken, so the fully written temp file
        // cannot be moved in (overwrite: false): the fault lands between
        // the temp write and the move.
        var existing = Path.Combine(_folder, ArchiveName(0));
        await File.WriteAllTextAsync(existing, "existing");

        var act = () => CreateLocalStorage().UploadAsync(
            new MemoryStream(RandomBytes(1024)), ArchiveName(0), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        Directory.EnumerateFiles(_folder).Should().ContainSingle();
        (await File.ReadAllTextAsync(existing)).Should().Be("existing");
        Directory.EnumerateFileSystemEntries(_tempDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task List_returns_only_mrz_files()
    {
        var storage = CreateLocalStorage();
        await storage.UploadAsync(
            new MemoryStream(RandomBytes(32)), ArchiveName(0), CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_folder, "notes.txt"), "x");
        await File.WriteAllTextAsync(Path.Combine(_folder, "archive.mrzx"), "x");
        await File.WriteAllTextAsync(Path.Combine(_folder, "archive.mrz.partial"), "x");

        var listed = await storage.ListAsync(CancellationToken.None);

        listed.Select(a => a.Name).Should().Equal(ArchiveName(0));
    }

    [Fact]
    public async Task List_on_a_missing_folder_is_empty()
    {
        var listed = await CreateUnavailableStorage().ListAsync(CancellationToken.None);

        listed.Should().BeEmpty();
    }

    [Fact]
    public async Task Folder_change_applies_without_recreating_the_storage()
    {
        var storage = CreateLocalStorage();
        var second = Path.Combine(_root, "cloud-2");
        Directory.CreateDirectory(second);

        _folder = second;
        var id = await storage.UploadAsync(
            new MemoryStream(RandomBytes(32)), ArchiveName(0), CancellationToken.None);

        Path.GetDirectoryName(id).Should().Be(second);
    }

    [Fact]
    public async Task CreatedAtUtc_is_the_file_last_write_time()
    {
        var storage = CreateLocalStorage();
        var id = await storage.UploadAsync(
            new MemoryStream(RandomBytes(32)), ArchiveName(0), CancellationToken.None);
        var lastWrite = DateTime.UtcNow.AddDays(-90);
        File.SetLastWriteTimeUtc(id, lastWrite);

        var listed = await storage.ListAsync(CancellationToken.None);

        listed.Single().CreatedAtUtc.Should().Be(new DateTimeOffset(lastWrite));
    }

    [Fact]
    public async Task Upload_rejects_a_name_that_is_not_a_plain_file_name()
    {
        var act = () => CreateLocalStorage().UploadAsync(
            new MemoryStream(RandomBytes(32)),
            Path.Combine("..", ArchiveName(0)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        Directory.EnumerateFileSystemEntries(_root).Should().HaveCount(2, "only the cloud and temp folders exist");
    }

    private LocalFolderArchiveStorage CreateLocalStorage() =>
        new(() => _folder, _tempDirectory, NullLogger<LocalFolderArchiveStorage>.Instance);
}
