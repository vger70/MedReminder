using FluentAssertions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Tests.Support;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Backup;

// Listing and retention of the automatic snapshots in an IArchiveStorage,
// shared by the desktop backup and the Android cloud backup (B2-03).
public sealed class CloudSnapshotsTests
{
    private const string Anna = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Bruno = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static string Snapshot(string profileId, int daysAgo) => CloudSnapshotName.Create(profileId, Now.AddDays(-daysAgo));

    [Fact]
    public async Task List_reads_profile_and_date_from_the_name_most_recent_first()
    {
        var storage = new MemoryArchiveStorage();
        storage.Add(Snapshot(Anna, 3), Now);
        storage.Add("my-export.mrz", Now.AddDays(-1));
        storage.Add(Snapshot(Bruno, 2), Now);

        var list = await CloudSnapshots.ListAsync(storage, CancellationToken.None);

        list.Select(s => s.FileName).Should().Equal("my-export.mrz", Snapshot(Bruno, 2), Snapshot(Anna, 3));
        list[0].ProfileId.Should().BeEmpty();
        list[0].CreatedAtUtc.Should().Be(Now.AddDays(-1));
        list[1].ProfileId.Should().Be(Bruno);
        list[1].CreatedAtUtc.Should().Be(Now.AddDays(-2));
    }

    [Fact]
    public async Task Prune_deletes_only_old_snapshots_and_leaves_other_names()
    {
        var storage = new MemoryArchiveStorage();
        storage.Add(Snapshot(Anna, 40), Now.AddDays(-40));
        storage.Add(Snapshot(Anna, 5), Now.AddDays(-5));
        storage.Add("my-export.mrz", Now.AddDays(-90));

        var deleted = await CloudSnapshots.PruneAsync(storage, 30, Now, profileIds: null, CancellationToken.None);

        deleted.Should().Be(1);
        storage.Names.Should().BeEquivalentTo(Snapshot(Anna, 5), "my-export.mrz");
    }

    [Fact]
    public async Task Prune_limited_to_profiles_keeps_the_others_old_snapshots()
    {
        var storage = new MemoryArchiveStorage();
        storage.Add(Snapshot(Anna, 40), Now.AddDays(-40));
        storage.Add(Snapshot(Bruno, 40), Now.AddDays(-40));

        var deleted = await CloudSnapshots.PruneAsync(storage, 30, Now, [Anna], CancellationToken.None);

        deleted.Should().Be(1);
        storage.Names.Should().BeEquivalentTo(Snapshot(Bruno, 40));
    }

    [Fact]
    public async Task A_failed_delete_does_not_stop_the_others()
    {
        var storage = new MemoryArchiveStorage();
        storage.Add(Snapshot(Anna, 40), Now.AddDays(-40));
        storage.Add(Snapshot(Bruno, 40), Now.AddDays(-40));
        storage.FailDelete = name => name.Contains(Anna) ? new IOException("locked") : null;

        var deleted = await CloudSnapshots.PruneAsync(storage, 30, Now, profileIds: null, CancellationToken.None);

        deleted.Should().Be(1);
        storage.Names.Should().BeEquivalentTo(Snapshot(Anna, 40));
    }

    [Fact]
    public async Task No_retention_prunes_nothing()
    {
        var storage = new MemoryArchiveStorage();
        storage.Add(Snapshot(Anna, 400), Now.AddDays(-400));

        (await CloudSnapshots.PruneAsync(storage, 0, Now, profileIds: null, CancellationToken.None)).Should().Be(0);
        storage.Names.Should().HaveCount(1);
    }
}
