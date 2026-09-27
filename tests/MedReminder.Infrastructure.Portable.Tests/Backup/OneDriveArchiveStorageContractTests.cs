using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.OneDrive;
using MedReminder.Infrastructure.Tests.Support;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Backup;

// The IArchiveStorage contract against OneDriveArchiveStorage over an
// in-memory Graph app folder (C.3++ Phase 2, B.1 Phase 4a), plus the
// OneDrive specifics: the backups/ folder, large archives through an
// upload session under a temporary name, and a signed-out account as the
// unavailable target.
public sealed class OneDriveArchiveStorageContractTests : ArchiveStorageContractTests
{
    private readonly FakeOneDrive _drive = new();

    protected override IArchiveStorage CreateStorage()
        => new OneDriveArchiveStorage(new OneDriveClient(_drive.CreateClient(), (_, _) => Task.FromResult("token")));

    protected override IArchiveStorage CreateUnavailableStorage()
        => new OneDriveArchiveStorage(new OneDriveClient(_drive.CreateClient(),
            (_, _) => throw new CloudSignInRequiredException(CloudProvider.OneDrive)));

    [Fact]
    public async Task Archives_live_in_the_backups_folder_next_to_sync()
    {
        _drive.Put("sync/g/group.json", [1]);
        var storage = CreateStorage();

        await storage.UploadAsync(new MemoryStream(RandomBytes(32)), ArchiveName(0), CancellationToken.None);

        _drive.Files().Should().Contain($"backups/{ArchiveName(0)}");
        (await storage.ListAsync(CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_large_archive_is_never_listed_half_uploaded()
    {
        var storage = CreateStorage();
        var payload = RandomBytes(OneDriveClient.SimpleUploadLimit + 2_000_000);

        var id = await storage.UploadAsync(new MemoryStream(payload), ArchiveName(0), CancellationToken.None);

        _drive.Files().Should().Equal($"backups/{ArchiveName(0)}");
        _drive.Content($"backups/{id}").Should().Equal(payload);
    }

    [Fact]
    public async Task Temporary_and_foreign_files_are_not_listed()
    {
        _drive.Put("backups/.tmp-123", []);
        _drive.Put("backups/notes.txt", [1]);

        (await CreateStorage().ListAsync(CancellationToken.None)).Should().BeEmpty();
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
