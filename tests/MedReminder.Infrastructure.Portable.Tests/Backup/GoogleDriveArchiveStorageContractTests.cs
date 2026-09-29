using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.GoogleDrive;
using MedReminder.Infrastructure.Tests.Support;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Backup;

// The IArchiveStorage contract against GoogleDriveArchiveStorage over an
// in-memory Drive (C.3++ Phase 2, B.1 Phase 4b), plus the Google Drive
// specifics: the visible MedReminder/backups folder found by property,
// resumable uploads for large archives, and a session that needs a new
// sign-in reaching the caller.
public sealed class GoogleDriveArchiveStorageContractTests : ArchiveStorageContractTests
{
    private readonly FakeGoogleDrive _drive = new();

    protected override IArchiveStorage CreateStorage()
        => new GoogleDriveArchiveStorage(new GoogleDriveClient(_drive.CreateClient(), (_, _) => Task.FromResult("token")));

    private IArchiveStorage CreateSignedOutStorage()
        => new GoogleDriveArchiveStorage(new GoogleDriveClient(_drive.CreateClient(),
            (_, _) => throw new CloudSignInRequiredException(CloudProvider.GoogleDrive)));

    [Fact]
    public async Task Archives_live_in_a_visible_MedReminder_backups_folder()
    {
        var storage = CreateStorage();

        await storage.UploadAsync(new MemoryStream(RandomBytes(32)), ArchiveName(0), CancellationToken.None);
        await storage.UploadAsync(new MemoryStream(RandomBytes(32)), ArchiveName(1), CancellationToken.None);

        _drive.Names("drive").Should().BeEquivalentTo(ArchiveName(0), ArchiveName(1));
        _drive.Names(FakeGoogleDrive.AppData).Should().BeEmpty();
        _drive.FolderIdByProperty(GoogleDriveArchiveStorage.FolderProperty, "root").Should().NotBeNull();
        _drive.FolderIdByProperty(GoogleDriveArchiveStorage.FolderProperty, "backups").Should().NotBeNull();
        (await storage.ListAsync(CancellationToken.None)).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_folder_of_the_user_with_the_same_name_is_not_used()
    {
        var own = _drive.Put(FakeGoogleDrive.Root, "MedReminder", [], folder: true);
        _drive.Put(own, "medreminder-00000000000000000000000000000000-20260101-000000.mrz", [1]);

        (await CreateStorage().ListAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Listing_before_the_first_backup_creates_nothing()
    {
        (await CreateStorage().ListAsync(CancellationToken.None)).Should().BeEmpty();

        _drive.FolderIdByProperty(GoogleDriveArchiveStorage.FolderProperty, "backups").Should().BeNull();
    }

    [Fact]
    public async Task A_large_archive_goes_through_a_resumable_upload()
    {
        var storage = CreateStorage();
        var payload = RandomBytes(GoogleDriveClient.MultipartLimit + 2_000_000);

        var id = await storage.UploadAsync(new MemoryStream(payload), ArchiveName(0), CancellationToken.None);

        await using var downloaded = await storage.DownloadAsync(id, CancellationToken.None);
        var copy = new MemoryStream();
        await downloaded.CopyToAsync(copy);
        copy.ToArray().Should().Equal(payload);
        downloaded.CanSeek.Should().BeFalse("the response body is streamed");
        _drive.Log.Should().Contain(l => l.Contains("uploadType=resumable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_and_list_with_an_ended_session_throw_CloudSignInRequiredException()
    {
        await FluentActions.Awaiting(() => CreateSignedOutStorage().UploadAsync(
                new MemoryStream(RandomBytes(16)), ArchiveName(0), CancellationToken.None))
            .Should().ThrowAsync<CloudSignInRequiredException>();
        await FluentActions.Awaiting(() => CreateSignedOutStorage().ListAsync(CancellationToken.None))
            .Should().ThrowAsync<CloudSignInRequiredException>();
    }

    [Fact]
    public async Task Downloading_a_missing_archive_throws_FileNotFoundException()
    {
        var act = () => CreateStorage().DownloadAsync("no-such-id", CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task An_existing_name_is_not_overwritten()
    {
        var storage = CreateStorage();
        await storage.UploadAsync(new MemoryStream(RandomBytes(16)), ArchiveName(0), CancellationToken.None);

        var act = () => storage.UploadAsync(new MemoryStream(RandomBytes(16)), ArchiveName(0), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
    }
}
