using System.Net;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.OneDrive;
using MedReminder.Infrastructure.Tests.Support;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Sync;

// OneDriveSyncTransport over an in-memory Graph app folder (B.1 Phase 4a,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5.8, spike S6 in §18.6).
public sealed class OneDriveSyncTransportTests : SyncTransportContractTests
{
    private readonly FakeOneDrive _drive = new();
    private readonly SyncDevice.SettableClock _clock = new(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly List<bool> _tokenRequests = [];

    protected override ISyncTransport CreateTransport() => new OneDriveSyncTransport(Client(), _clock);

    private OneDriveClient Client() => new(_drive.CreateClient(), (force, _) =>
    {
        _tokenRequests.Add(force);
        return Task.FromResult(force ? "fresh-token" : "token");
    }, _clock);

    private static byte[] Bytes(int size, byte seed = 1) => [.. Enumerable.Range(0, size).Select(i => (byte)(i * 31 + seed))];

    private void Later() => _clock.Advance(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Files_live_under_the_sync_folder_of_the_app_folder()
    {
        var transport = CreateTransport();
        await transport.CreateAsync("g/group.json", Bytes(10), default);

        _drive.Files().Should().Equal("sync/g/group.json");
    }

    [Fact]
    public async Task A_large_file_is_uploaded_under_a_temporary_name_and_renamed()
    {
        var transport = CreateTransport();
        var content = Bytes(OneDriveClient.SimpleUploadLimit + 1_000_000);

        (await transport.CreateAsync("g/genesis/1.mrg", content, default)).Should().BeTrue();
        (await transport.CreateAsync("g/genesis/1.mrg", Bytes(OneDriveClient.SimpleUploadLimit + 10, 7), default)).Should().BeFalse();

        _drive.Files().Should().Equal("sync/g/genesis/1.mrg");
        _drive.Content("sync/g/genesis/1.mrg").Should().Equal(content);
        _drive.Log.Should().Contain(l => l.Contains("createUploadSession", StringComparison.Ordinal)
            && l.Contains(OneDriveClient.TempPrefix, StringComparison.Ordinal));
        // The pre-authenticated upload URL gets no bearer token.
        _drive.UploadAuthorizations.Should().NotBeEmpty().And.OnlyContain(a => a == null);
    }

    [Fact]
    public async Task A_large_write_replaces_the_file()
    {
        var transport = CreateTransport();
        await transport.WriteAsync("g/devices/d.mrd", Bytes(OneDriveClient.SimpleUploadLimit + 5), default);
        var second = Bytes(OneDriveClient.SimpleUploadLimit + 9, 3);
        await transport.WriteAsync("g/devices/d.mrd", second, default);

        _drive.Files().Should().Equal("sync/g/devices/d.mrd");
        (await transport.ReadAsync("g/devices/d.mrd", default)).Should().Equal(second);
    }

    [Fact]
    public async Task Files_of_other_devices_appear_through_the_change_feed()
    {
        var transport = CreateTransport();
        (await transport.ListAsync(string.Empty, default)).Should().BeEmpty();

        _drive.Put("sync/g/ops/1/b/1.mrs", Bytes(5));
        _drive.Put("backups/x.mrz", Bytes(5));
        Later();

        (await transport.ListAsync(string.Empty, default)).Should().Equal("g/ops/1/b/1.mrs");
    }

    [Fact]
    public async Task Only_the_sync_folder_is_listed_and_the_list_follows_later_changes()
    {
        var transport = CreateTransport();
        _drive.Put("sync/g/group.json", Bytes(5));
        _drive.Put("sync-old/g/group.json", Bytes(5));
        _drive.Put("group.json", Bytes(5));
        (await transport.ListAsync(string.Empty, default)).Should().Equal("g/group.json");

        // A backup-only change, then a sync change: the resolved list is
        // rebuilt from the index each time the feed changes it.
        _drive.Put("backups/x.mrz", Bytes(5));
        Later();
        (await transport.ListAsync(string.Empty, default)).Should().Equal("g/group.json");
        _drive.Put("sync/g/devices/b.mrd", Bytes(5));
        Later();
        (await transport.ListAsync(string.Empty, default)).Should().Equal("g/devices/b.mrd", "g/group.json");
    }

    [Fact]
    public async Task Temporary_and_in_progress_files_are_not_listed()
    {
        var transport = CreateTransport();
        _drive.Put("sync/g/checkpoints/1/.tmp-abc", []);
        _drive.Put("sync/g/ops/1/b/1.mrs", Bytes(5));
        Later();

        (await transport.ListAsync("g/", default)).Should().Equal("g/ops/1/b/1.mrs");
    }

    [Fact]
    public async Task Own_writes_and_deletes_are_listed_even_when_the_feed_lags()
    {
        var transport = CreateTransport();
        await transport.CreateAsync("g/ops/1/a/1.mrs", Bytes(5), default);
        await transport.CreateAsync("g/ops/1/a/2.mrs", Bytes(5), default);
        Later();
        (await transport.ListAsync("g/", default)).Should().Equal("g/ops/1/a/1.mrs", "g/ops/1/a/2.mrs");

        _drive.DeltaReportsChanges = false;
        await transport.DeleteAsync("g/ops/1/a/1.mrs", default);
        await transport.CreateAsync("g/ops/1/a/3.mrs", Bytes(5), default);
        Later();

        (await transport.ListAsync("g/", default)).Should().Equal("g/ops/1/a/2.mrs", "g/ops/1/a/3.mrs");
    }

    [Fact]
    public async Task Children_of_a_deleted_folder_disappear()
    {
        var transport = CreateTransport();
        _drive.Put("sync/g/ops/1/b/1.mrs", Bytes(5));
        _drive.Put("sync/g/ops/1/b/2.mrs", Bytes(5));
        _drive.Put("sync/g/devices/b.mrd", Bytes(5));
        (await transport.ListAsync("g/", default)).Should().HaveCount(3);

        _drive.Remove("sync/g/ops");
        Later();

        (await transport.ListAsync("g/", default)).Should().Equal("g/devices/b.mrd");
    }

    [Fact]
    public async Task An_expired_change_cursor_starts_a_full_enumeration()
    {
        var transport = CreateTransport();
        _drive.Put("sync/g/ops/1/b/1.mrs", Bytes(5));
        (await transport.ListAsync("g/", default)).Should().ContainSingle();

        _drive.Put("sync/g/ops/1/b/2.mrs", Bytes(5));
        _drive.ExpireDeltaOnce = true;
        Later();

        (await transport.ListAsync("g/", default)).Should().Equal("g/ops/1/b/1.mrs", "g/ops/1/b/2.mrs");
    }

    [Fact]
    public async Task The_feed_is_followed_across_pages()
    {
        _drive.DeltaPageSize = 1;
        var transport = CreateTransport();
        for (var i = 1; i <= 5; i++) _drive.Put($"sync/g/ops/1/b/{i}.mrs", Bytes(5));

        (await transport.ListAsync("g/ops/", default)).Should().HaveCount(5);
    }

    [Fact]
    public async Task Throttling_is_retried_after_the_advertised_delay()
    {
        var transport = CreateTransport();
        _drive.Failures.Enqueue((HttpStatusCode.TooManyRequests, TimeSpan.Zero));
        _drive.Failures.Enqueue((HttpStatusCode.ServiceUnavailable, TimeSpan.Zero));

        (await transport.CreateAsync("g/group.json", Bytes(3), default)).Should().BeTrue();
    }

    [Fact]
    public async Task A_rejected_token_is_refreshed_once()
    {
        var transport = CreateTransport();
        _drive.RejectNextToken = true;

        (await transport.CreateAsync("g/group.json", Bytes(3), default)).Should().BeTrue();
        _tokenRequests.Should().Equal(false, true);
    }

    [Fact]
    public async Task A_persistent_failure_reports_the_status_without_the_token()
    {
        var transport = CreateTransport();
        for (var i = 0; i < 10; i++) _drive.Failures.Enqueue((HttpStatusCode.BadRequest, null));

        var failure = await FluentActions.Awaiting(() => transport.ReadAsync("g/group.json", default))
            .Should().ThrowAsync<CloudStorageException>();
        failure.Which.Status.Should().Be(HttpStatusCode.BadRequest);
        failure.Which.Message.Should().NotContain("token");
    }

    [Fact]
    public async Task Invalid_paths_are_refused()
    {
        var transport = CreateTransport();

        await FluentActions.Awaiting(() => transport.ReadAsync("../outside", default)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => transport.ReadAsync("g//x", default)).Should().ThrowAsync<ArgumentException>();
    }
}
