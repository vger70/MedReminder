using System.Net;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Cloud.GoogleDrive;
using MedReminder.Infrastructure.Cloud.OneDrive;
using MedReminder.Infrastructure.Tests.Support;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Sync;

// GoogleDriveSyncTransport over an in-memory Drive (B.1 Phase 4b,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5.8, spike S7 in §18.7).
public sealed class GoogleDriveSyncTransportTests : SyncTransportContractTests
{
    private static readonly Dictionary<string, string> SyncTag = new() { [GoogleDriveSyncTransport.SyncProperty] = "1" };

    private readonly FakeGoogleDrive _drive = new();
    private readonly SyncDevice.SettableClock _clock = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly List<bool> _tokenRequests = [];

    protected override ISyncTransport CreateTransport() => new GoogleDriveSyncTransport(Client(), _clock);

    private GoogleDriveClient Client() => new(_drive.CreateClient(), (force, _) =>
    {
        _tokenRequests.Add(force);
        return Task.FromResult(force ? "fresh-token" : "token");
    }, _clock);

    private static byte[] Bytes(int size, byte seed = 1) => [.. Enumerable.Range(0, size).Select(i => (byte)(i * 31 + seed))];

    private void Later() => _clock.Advance(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Files_are_flat_in_the_app_data_folder_named_by_path()
    {
        var transport = CreateTransport();
        await transport.CreateAsync("g/ops/1/d/1.mrs", Bytes(10), default);

        _drive.Names(FakeGoogleDrive.AppData).Should().Equal("g/ops/1/d/1.mrs");
        _drive.Names("drive").Should().BeEmpty();
    }

    [Fact]
    public async Task A_large_file_goes_through_a_resumable_upload()
    {
        var transport = CreateTransport();
        var content = Bytes(GoogleDriveClient.MultipartLimit + 3_000_000);

        (await transport.CreateAsync("g/genesis/1.mrg", content, default)).Should().BeTrue();
        await transport.WriteAsync("g/devices/d.mrd", content, default);
        var replaced = Bytes(GoogleDriveClient.MultipartLimit + 10, 7);
        await transport.WriteAsync("g/devices/d.mrd", replaced, default);

        _drive.ContentOf(FakeGoogleDrive.AppData, "g/genesis/1.mrg").Should().Equal(content);
        (await transport.ReadAsync("g/devices/d.mrd", default)).Should().Equal(replaced);
        _drive.Log.Should().Contain(l => l.Contains("uploadType=resumable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Files_of_other_devices_are_listed_and_untagged_files_are_not()
    {
        var transport = CreateTransport();
        (await transport.ListAsync(string.Empty, default)).Should().BeEmpty();

        _drive.Put(FakeGoogleDrive.AppData, "g/ops/1/b/1.mrs", Bytes(5), SyncTag);
        _drive.Put(FakeGoogleDrive.AppData, "unrelated.bin", Bytes(5));
        Later();

        (await transport.ListAsync(string.Empty, default)).Should().Equal("g/ops/1/b/1.mrs");
        (await transport.ReadAsync("g/ops/1/b/1.mrs", default)).Should().Equal(Bytes(5));
    }

    [Fact]
    public async Task A_file_created_by_another_device_first_makes_the_create_fail()
    {
        var transport = CreateTransport();
        _drive.Put(FakeGoogleDrive.AppData, "g/group.json", Bytes(3), SyncTag);

        (await transport.CreateAsync("g/group.json", Bytes(4), default)).Should().BeFalse();
        _drive.Names(FakeGoogleDrive.AppData).Should().ContainSingle();
        // Checked before uploading: nothing was sent.
        _drive.Log.Should().NotContain(l => l.Contains("/upload/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Among_duplicate_names_every_device_reads_the_oldest()
    {
        _drive.Put(FakeGoogleDrive.AppData, "g/genesis/2.mrg", Bytes(3, 1), SyncTag);
        _drive.Put(FakeGoogleDrive.AppData, "g/genesis/2.mrg", Bytes(3, 2), SyncTag);

        var a = CreateTransport();
        var b = new GoogleDriveSyncTransport(Client(), _clock);

        (await a.ReadAsync("g/genesis/2.mrg", default)).Should().Equal(Bytes(3, 1));
        (await b.ReadAsync("g/genesis/2.mrg", default)).Should().Equal(Bytes(3, 1));
        (await a.ListAsync("g/", default)).Should().Equal("g/genesis/2.mrg");
    }

    [Fact]
    public async Task Own_writes_and_deletes_are_listed_even_when_the_listing_lags()
    {
        var transport = CreateTransport();
        await transport.CreateAsync("g/ops/1/a/1.mrs", Bytes(5), default);
        Later();
        (await transport.ListAsync("g/", default)).Should().Equal("g/ops/1/a/1.mrs");

        _drive.ListingLags = true;
        await transport.CreateAsync("g/ops/1/a/2.mrs", Bytes(5), default);
        await transport.DeleteAsync("g/ops/1/a/1.mrs", default);
        Later();

        (await transport.ListAsync("g/", default)).Should().Equal("g/ops/1/a/2.mrs");
        (await transport.ReadAsync("g/ops/1/a/2.mrs", default)).Should().Equal(Bytes(5));
        (await transport.ReadAsync("g/ops/1/a/1.mrs", default)).Should().BeNull();
    }

    [Fact]
    public async Task A_file_written_during_a_listing_lag_appears_once_the_listing_catches_up()
    {
        var reader = CreateTransport();
        var writer = new GoogleDriveSyncTransport(Client(), _clock);
        (await reader.ListAsync(string.Empty, default)).Should().BeEmpty();

        _drive.ListingLags = true;
        await writer.WriteAsync("g/devices/c.mrd", Bytes(3), default);
        Later();
        (await reader.ListAsync(string.Empty, default)).Should().BeEmpty();

        _drive.ListingLags = false;
        Later();
        (await reader.ListAsync(string.Empty, default)).Should().Equal("g/devices/c.mrd");
    }

    [Fact]
    public async Task A_create_retried_after_a_lost_response_is_recognised()
    {
        var transport = CreateTransport();
        // The create reached the server but its response was lost: the
        // retry with the same pre-generated id gets 409 and must return
        // the existing file, not fail or duplicate it.
        var client = Client();
        var id = await client.GenerateIdAsync(GoogleDriveClient.AppDataFolder, default);
        await client.CreateAsync(id, "g/x.mrs", GoogleDriveClient.AppDataFolder, SyncTag, new MemoryStream(Bytes(3)), 3, default);

        var again = await client.CreateAsync(id, "g/x.mrs", GoogleDriveClient.AppDataFolder, SyncTag, new MemoryStream(Bytes(3)), 3, default);

        again.Id.Should().Be(id);
        _drive.Names(FakeGoogleDrive.AppData).Should().ContainSingle();
        (await transport.ListAsync(string.Empty, default)).Should().Equal("g/x.mrs");
    }

    [Fact]
    public async Task The_listing_follows_pages()
    {
        _drive.PageSize = 2;
        for (var i = 1; i <= 5; i++) _drive.Put(FakeGoogleDrive.AppData, $"g/ops/1/b/{i}.mrs", Bytes(5), SyncTag);

        (await CreateTransport().ListAsync("g/ops/", default)).Should().HaveCount(5);
    }

    [Fact]
    public async Task Throttling_and_rate_limits_are_retried()
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

        (await transport.ListAsync(string.Empty, default)).Should().BeEmpty();
        _tokenRequests.Should().Equal(false, true);
    }

    [Fact]
    public async Task A_persistent_failure_reports_the_status_and_the_provider()
    {
        var transport = CreateTransport();
        for (var i = 0; i < 10; i++) _drive.Failures.Enqueue((HttpStatusCode.BadRequest, null));

        var failure = await FluentActions.Awaiting(() => transport.ListAsync(string.Empty, default))
            .Should().ThrowAsync<CloudStorageException>();
        failure.Which.Status.Should().Be(HttpStatusCode.BadRequest);
        failure.Which.Message.Should().StartWith("Google Drive request failed").And.NotContain("token");
    }

    [Fact]
    public async Task Invalid_paths_are_refused()
    {
        var transport = CreateTransport();

        await FluentActions.Awaiting(() => transport.ReadAsync("../outside", default)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => transport.ReadAsync("g//x", default)).Should().ThrowAsync<ArgumentException>();
    }
}
