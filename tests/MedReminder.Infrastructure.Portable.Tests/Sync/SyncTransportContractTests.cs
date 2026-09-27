using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Sync;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Sync;

// Contract of ISyncTransport (B.1 Phase 3c, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §5.8). Every transport runs the same tests;
// the provider transports of Phase 4 derive from this class too.
public abstract class SyncTransportContractTests
{
    protected abstract ISyncTransport CreateTransport();

    private static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task Create_never_replaces_a_file()
    {
        var transport = CreateTransport();

        (await transport.CreateAsync("g/ops/1/d/1.mrs", Bytes("first"), default)).Should().BeTrue();
        (await transport.CreateAsync("g/ops/1/d/1.mrs", Bytes("second"), default)).Should().BeFalse();

        (await transport.ReadAsync("g/ops/1/d/1.mrs", default)).Should().Equal(Bytes("first"));
    }

    [Fact]
    public async Task Write_replaces_and_read_of_a_missing_file_is_null()
    {
        var transport = CreateTransport();
        await transport.WriteAsync("g/devices/d.mrd", Bytes("v1"), default);
        await transport.WriteAsync("g/devices/d.mrd", Bytes("v2"), default);

        (await transport.ReadAsync("g/devices/d.mrd", default)).Should().Equal(Bytes("v2"));
        (await transport.ReadAsync("g/devices/none.mrd", default)).Should().BeNull();
    }

    [Fact]
    public async Task List_is_recursive_filtered_by_prefix_and_ordered()
    {
        var transport = CreateTransport();
        foreach (var path in new[] { "g/ops/1/b/2.mrs", "g/ops/1/a/1.mrs", "g/genesis/1.mrg", "h/group.json" })
        {
            await transport.CreateAsync(path, Bytes(path), default);
        }

        (await transport.ListAsync("g/ops/", default)).Should().Equal("g/ops/1/a/1.mrs", "g/ops/1/b/2.mrs");
        (await transport.ListAsync(string.Empty, default)).Should().HaveCount(4);
        (await transport.ListAsync("x/", default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_removes_and_tolerates_a_missing_file()
    {
        var transport = CreateTransport();
        await transport.CreateAsync("g/ops/1/d/1.mrs", Bytes("x"), default);

        await transport.DeleteAsync("g/ops/1/d/1.mrs", default);
        await transport.DeleteAsync("g/ops/1/d/1.mrs", default);

        (await transport.ListAsync("g/", default)).Should().BeEmpty();
    }
}

public sealed class LocalFolderSyncTransportTests : SyncTransportContractTests, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-transport-" + Guid.NewGuid().ToString("N"));

    protected override ISyncTransport CreateTransport() => new LocalFolderSyncTransport(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Files_being_written_are_not_listed()
    {
        var transport = CreateTransport();
        Directory.CreateDirectory(Path.Combine(_root, "g"));
        await File.WriteAllTextAsync(Path.Combine(_root, "g", ".tmp-123"), "partial");

        (await transport.ListAsync(string.Empty, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Paths_cannot_leave_the_folder()
    {
        var transport = CreateTransport();

        await FluentActions.Awaiting(() => transport.ReadAsync("../outside.txt", default))
            .Should().ThrowAsync<ArgumentException>();
    }
}
