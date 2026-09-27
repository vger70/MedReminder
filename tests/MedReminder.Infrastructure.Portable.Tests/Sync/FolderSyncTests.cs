using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Sync;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace MedReminder.Infrastructure.Tests.Sync;

// B.1 Phase 3c end to end: devices on real SQLite databases synchronized
// only through a folder (LocalFolderSyncTransport), with the group key,
// encrypted segments, the causal buffer, checkpoints, compaction and
// generations (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5).
public sealed class FolderSyncTests : IDisposable
{
    private const string Passphrase = "sync passphrase for tests";
    private const string SecretNote = "Take with the evening meal";
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly ITestOutputHelper _output;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-folder-" + Guid.NewGuid().ToString("N"));
    private readonly List<SyncDevice> _devices = new();

    public FolderSyncTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    private string Folder => Path.Combine(_root, "remote");

    public void Dispose()
    {
        foreach (var d in _devices) d.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<SyncDevice> CreateGroupAsync(int checkpointEvery = 5000, string name = "A")
    {
        var device = Track(new SyncDevice(name, Path.Combine(_root, $"{name}.db"), Now, settings: null,
            checkpointEvery: checkpointEvery));
        await device.InitializeAsync();
        await device.RunAsync(sp => sp.GetRequiredService<AddMedicine>().ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "tablet", 1m, 2, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            Notes: SecretNote, InitialQuantity: 60m), CancellationToken.None));
        await device.RunAsync(sp => sp.GetRequiredService<CreateSyncGroup>().ExecuteAsync(
            new LocalFolderSyncTransport(Folder), Passphrase.ToCharArray(), Folder, CancellationToken.None,
            SyncFileFormatTests.FastKdf));
        return device;
    }

    private async Task<SyncDevice> JoinAsync(SyncDevice via, string name, int checkpointEvery = 5000,
        Func<ISyncTransport, ISyncTransport>? transport = null)
    {
        var path = Path.Combine(_root, $"{name}.db");
        var groupId = via.Settings.Load()!.GroupId;
        var joined = await via.RunAsync(sp => sp.GetRequiredService<JoinSyncGroup>().ExecuteAsync(
            new LocalFolderSyncTransport(Folder), groupId, Passphrase.ToCharArray(), path, Folder, CancellationToken.None));
        var device = Track(new SyncDevice(name, path, via.Clock.GetUtcNow(), joined.Settings, joined.Key,
            checkpointEvery, transport));
        await device.InitializeAsync();
        return device;
    }

    private SyncDevice Track(SyncDevice device)
    {
        _devices.Add(device);
        return device;
    }

    private static async Task SyncAllAsync(IReadOnlyList<SyncDevice> devices, int rounds = 3)
    {
        for (var round = 0; round < rounds; round++)
        {
            foreach (var d in devices)
            {
                var result = await d.SyncAsync();
                result.NewerGeneration.Should().BeNull();
                result.RebuildRequired.Should().BeFalse(d.Name);
            }
        }
    }

    private static async Task<List<string>> StatesAsync(IReadOnlyList<SyncDevice> devices)
    {
        var end = devices.Max(d => d.Clock.GetUtcNow()).AddMinutes(1);
        var states = new List<string>();
        foreach (var d in devices)
        {
            d.Clock.Set(end);
            await d.RunAsync(sp => sp.GetRequiredService<ConsumptionCatchUp>().RunAsync(CancellationToken.None));
            states.Add(await SyncStateDescriber.DescribeAsync(d));
        }
        return states;
    }

    private void ShouldConverge(IReadOnlyList<string> states)
    {
        foreach (var state in states.Skip(1))
        {
            if (state != states[0])
            {
                var left = states[0].Split('\n').ToHashSet();
                var right = state.Split('\n').ToHashSet();
                _output.WriteLine(string.Join('\n', left.Except(right).Select(l => "- " + l)
                    .Concat(right.Except(left).Select(l => "+ " + l))));
            }
            state.Should().Be(states[0]);
        }
    }

    [Fact]
    public async Task A_joined_device_starts_from_the_genesis()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");

        ShouldConverge(await StatesAsync([a, b]));
        (await b.RunAsync(sp => sp.GetRequiredService<IMedicineRepository>().ListAllAsync(default)))
            .Should().ContainSingle().Which.Notes.Should().Be(SecretNote);
    }

    [Fact]
    public async Task A_wrong_passphrase_cannot_join()
    {
        var a = await CreateGroupAsync();

        await FluentActions.Awaiting(() => a.RunAsync(sp => sp.GetRequiredService<JoinSyncGroup>().ExecuteAsync(
                new LocalFolderSyncTransport(Folder), a.Settings.Load()!.GroupId, "wrong".ToCharArray(),
                Path.Combine(_root, "X.db"), Folder, CancellationToken.None)))
            .Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Random_workloads_converge_through_the_folder_with_checkpoints_and_a_late_joiner()
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("SYNC_FOLDER_SEEDS"), out var n) ? n : 4;
        var from = int.TryParse(Environment.GetEnvironmentVariable("SYNC_FOLDER_FIRST_SEED"), out var f) ? f : 1;
        var checkpointJoins = 0;
        for (var seed = from; seed < from + seeds; seed++)
        {
            var random = new Random(seed);
            var a = await CreateGroupAsync(checkpointEvery: 15, name: $"A{seed}");
            var devices = new List<SyncDevice> { a, await JoinAsync(a, $"B{seed}", 15), await JoinAsync(a, $"C{seed}", 15) };
            var deleted = 0;
            var checkpoints = 0;

            for (var step = 0; step < 50; step++)
            {
                var device = devices[random.Next(devices.Count)];
                if (random.Next(4) == 0)
                {
                    var result = await device.SyncAsync();
                    result.RebuildRequired.Should().BeFalse(
                        $"seed {seed}: {device.Name} at step {step}: {string.Join("; ", result.Problems)}");
                    deleted += result.SegmentsDeleted;
                    checkpoints += result.CheckpointWritten ? 1 : 0;
                }
                else
                {
                    await SyncConvergenceTests.ActAsync(device, random);
                }
                device.Clock.Advance(TimeSpan.FromMinutes(random.Next(1, 120)));
                if (step == 35)
                {
                    // A late device: its bootstrap image is a checkpoint when
                    // segments were already deleted.
                    await device.SyncAsync();
                    if (deleted > 0) checkpointJoins++;
                    devices.Add(await JoinAsync(device, $"D{seed}", 15));
                }
            }

            await SyncAllAsync(devices);
            ShouldConverge(await StatesAsync(devices));
            _output.WriteLine($"seed {seed}: {checkpoints} checkpoints, {deleted} segments deleted.");
        }
        // Guard against a vacuous run: some late joiner must have started
        // from a checkpoint, with segments already deleted.
        checkpointJoins.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task The_folder_holds_no_plaintext()
    {
        var a = await CreateGroupAsync(checkpointEvery: 3);
        var b = await JoinAsync(a, "B", 3);
        var id = (await a.RunAsync(sp => sp.GetRequiredService<IMedicineRepository>().ListAllAsync(default)))[0].Id;
        await b.RunAsync(sp => sp.GetRequiredService<UpdateMedicine>().ExecuteAsync(new UpdateMedicineCommand(
            id, "Enalapril", "enalapril maleate", null, "tablet", 7, NotificationChannels.Windows, null, "Dr. Rossi",
            SecretNote + " and water", true), default));
        await a.RunAsync(sp => sp.GetRequiredService<AddStock>().ExecuteAsync(
            new AddStockCommand(id, 28m, StockMovementKind.NewPackage, "pharmacy on Main Street"), default));
        await SyncAllAsync([a, b]);

        var forbidden = new[] { "Enalapril", "enalapril", SecretNote, "Rossi", "Main Street", "tablet" };
        var files = Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories).ToList();
        files.Should().Contain(f => f.EndsWith(".mrc", StringComparison.Ordinal), "the test must cover checkpoints too");
        foreach (var file in files)
        {
            var bytes = await File.ReadAllBytesAsync(file);
            foreach (var word in forbidden)
            {
                ContainsBytes(bytes, Encoding.UTF8.GetBytes(word)).Should().BeFalse($"{word} must not be readable in {file}");
                ContainsBytes(bytes, Encoding.Unicode.GetBytes(word)).Should().BeFalse($"{word} must not be readable in {file}");
            }
            var relative = Path.GetRelativePath(Folder, file).Replace('\\', '/');
            relative.Should().MatchRegex(
                "^[0-9a-f]{32}/(group\\.json|key\\.\\d+\\.wrap|genesis/\\d+\\.mrg|checkpoints/\\d+/[0-9a-f]{32}-\\d+\\.mrc" +
                "|ops/\\d+/[0-9a-f]{32}/\\d+\\.mrs|devices/[0-9a-f]{32}\\.mrd)$");
        }
    }

    [Fact]
    public async Task A_new_generation_makes_the_other_devices_rebuild()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");
        var id = (await a.RunAsync(sp => sp.GetRequiredService<IMedicineRepository>().ListAllAsync(default)))[0].Id;
        await b.RunAsync(sp => sp.GetRequiredService<AddStock>().ExecuteAsync(
            new AddStockCommand(id, 10m, StockMovementKind.ManualAdd), default));
        await SyncAllAsync([a, b]);

        // An import or a restore on A (not simulated here) starts generation 2.
        var reset = await a.RunAsync(sp => sp.GetRequiredService<ResetSyncGeneration>().ExecuteAsync(
            new LocalFolderSyncTransport(Folder), CancellationToken.None));
        reset.Generation.Should().Be(2);
        await a.RunAsync(sp => sp.GetRequiredService<AddStock>().ExecuteAsync(
            new AddStockCommand(id, 5m, StockMovementKind.ManualAdd), default));

        var seen = await b.SyncAsync();
        seen.NewerGeneration.Should().Be(2);

        var path = Path.Combine(_root, "B2.db");
        var settings = await b.RunAsync(sp => sp.GetRequiredService<JoinSyncGroup>().RejoinAsync(
            new LocalFolderSyncTransport(Folder), b.Settings.Load()!, b.Keys.Load(reset.GroupId, 1)!, path, CancellationToken.None));
        var rebuilt = Track(new SyncDevice("B2", path, b.Clock.GetUtcNow(), settings, b.Keys.Load(reset.GroupId, 1)));
        await rebuilt.InitializeAsync();

        await SyncAllAsync([a, rebuilt]);
        ShouldConverge(await StatesAsync([a, rebuilt]));
        settings.DeviceId.Should().Be(b.Settings.Load()!.DeviceId);
    }

    [Fact]
    public async Task A_tampered_segment_is_reported_and_not_applied()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");
        var id = (await a.RunAsync(sp => sp.GetRequiredService<IMedicineRepository>().ListAllAsync(default)))[0].Id;
        await b.RunAsync(sp => sp.GetRequiredService<AddStock>().ExecuteAsync(
            new AddStockCommand(id, 10m, StockMovementKind.ManualAdd), default));
        await b.SyncAsync();

        var segment = Directory.EnumerateFiles(Folder, "*.mrs", SearchOption.AllDirectories).Single();
        var bytes = await File.ReadAllBytesAsync(segment);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(segment, bytes);

        var result = await a.SyncAsync();

        result.SegmentsApplied.Should().Be(0);
        result.Problems.Should().ContainSingle(p => p.Contains("cannot be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_segment_waits_until_its_dependencies_are_applied()
    {
        var hideA = true;
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");
        var aDevice = a.Settings.Load()!.DeviceId;
        var c = await JoinAsync(a, "C", transport: inner => new HidingTransport(inner, $"/{aDevice:N}/", () => hideA));
        var id = (await a.RunAsync(sp => sp.GetRequiredService<IMedicineRepository>().ListAllAsync(default)))[0].Id;

        await a.RunAsync(sp => sp.GetRequiredService<AddStock>().ExecuteAsync(
            new AddStockCommand(id, 28m, StockMovementKind.NewPackage), default));
        await a.SyncAsync();
        await b.SyncAsync(); // applies A's segment
        await b.RunAsync(sp => sp.GetRequiredService<AdjustStockDown>().ExecuteAsync(
            new AdjustStockDownCommand(id, 3m), default));
        await b.SyncAsync(); // B's segment depends on A's

        var blocked = await c.SyncAsync();
        blocked.SegmentsApplied.Should().Be(0);
        blocked.Problems.Should().Contain(p => p.Contains("waits for its dependencies", StringComparison.Ordinal));

        hideA = false;
        var released = await c.SyncAsync();
        released.SegmentsApplied.Should().Be(2);
        await SyncAllAsync([a, b, c]);
        ShouldConverge(await StatesAsync([a, b, c]));
    }

    [Fact]
    public async Task The_devices_list_shows_every_device_by_name()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");
        await SyncAllAsync([a, b], rounds: 1);

        var devices = await a.RunAsync(sp => sp.GetRequiredService<SyncEngine>().ListDevicesAsync(CancellationToken.None));

        devices.Select(d => d.Name).Should().BeEquivalentTo(["A", "B"]);
        devices.Should().OnlyContain(d => d.Platform == "desktop");
    }

    [Fact]
    public async Task A_pending_reset_blocks_the_engine_until_the_new_generation_starts()
    {
        var a = await CreateGroupAsync();
        var settings = a.Settings.Load()!;
        a.Settings.Save(settings with { ResetPending = true });

        await FluentActions.Awaiting(() => a.SyncAsync()).Should().ThrowAsync<InvalidOperationException>();

        var reset = await a.RunAsync(sp => sp.GetRequiredService<ResetSyncGeneration>().ExecuteAsync(
            new LocalFolderSyncTransport(Folder), CancellationToken.None));
        reset.ResetPending.Should().BeFalse();
        reset.Generation.Should().Be(2);
        (await a.SyncAsync()).Problems.Should().BeEmpty();
    }

    [Fact]
    public async Task A_clock_far_ahead_is_reported_and_its_changes_still_applied()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");
        var id = (await a.RunAsync(sp => sp.GetRequiredService<IMedicineRepository>().ListAllAsync(default)))[0].Id;
        b.Clock.Advance(TimeSpan.FromDays(2));
        await b.RunAsync(sp => sp.GetRequiredService<AddStock>().ExecuteAsync(
            new AddStockCommand(id, 10m, StockMovementKind.ManualAdd), default));
        await b.SyncAsync();

        var result = await a.SyncAsync();

        result.OperationsApplied.Should().Be(1);
        result.Problems.Should().ContainSingle(p => p.Contains("more than 24 hours ahead", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disabling_sync_keeps_the_data_and_forgets_the_group()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");
        var groupId = b.Settings.Load()!.GroupId;

        await b.RunAsync(sp => sp.GetRequiredService<DisableSync>().ExecuteAsync(CancellationToken.None));

        b.Settings.Load().Should().BeNull();
        b.Keys.Load(groupId, 1).Should().BeNull();
        (await b.RunAsync(sp => sp.GetRequiredService<IMedicineRepository>().ListAllAsync(default))).Should().ContainSingle();
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
        => haystack.AsSpan().IndexOf(needle) >= 0;

    // Hides the files whose path contains a marker, as a slow sync client
    // that has not downloaded them yet.
    private sealed class HidingTransport(ISyncTransport inner, string marker, Func<bool> hide) : ISyncTransport
    {
        public async Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct)
            => [.. (await inner.ListAsync(prefix, ct)).Where(p => !(hide() && p.Contains(marker, StringComparison.Ordinal)))];

        public Task<byte[]?> ReadAsync(string path, CancellationToken ct)
            => hide() && path.Contains(marker, StringComparison.Ordinal) ? Task.FromResult<byte[]?>(null) : inner.ReadAsync(path, ct);

        public Task<bool> CreateAsync(string path, byte[] content, CancellationToken ct) => inner.CreateAsync(path, content, ct);

        public Task WriteAsync(string path, byte[] content, CancellationToken ct) => inner.WriteAsync(path, content, ct);

        public Task DeleteAsync(string path, CancellationToken ct) => inner.DeleteAsync(path, ct);
    }
}
