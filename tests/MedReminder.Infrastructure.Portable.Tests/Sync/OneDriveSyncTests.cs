using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Sync;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Infrastructure.Cloud.OneDrive;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace MedReminder.Infrastructure.Tests.Sync;

// Devices that share a group only through a OneDrive app folder (B.1
// Phase 4a): the same engine as FolderSyncTests, with each device's own
// OneDriveSyncTransport and change-feed index over one in-memory Graph.
public sealed class OneDriveSyncTests : IDisposable
{
    private const string Passphrase = "sync passphrase for tests";
    private const string Account = "account-1";
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly ITestOutputHelper _output;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-onedrive-" + Guid.NewGuid().ToString("N"));
    private readonly FakeOneDrive _drive = new() { DeltaPageSize = 3 };
    private readonly List<SyncDevice> _devices = [];

    public OneDriveSyncTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var d in _devices) d.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static SyncTarget Target => SyncTarget.ForCloud(CloudProvider.OneDrive, Account);

    private OneDriveSyncTransport Transport(TimeProvider clock)
        => new(new OneDriveClient(_drive.CreateClient(), (_, _) => Task.FromResult("token"), clock), clock);

    private async Task<SyncDevice> CreateGroupAsync(string name, int checkpointEvery)
    {
        var device = Track(new SyncDevice(name, Path.Combine(_root, $"{name}.db"), Now, settings: null,
            checkpointEvery: checkpointEvery, remote: Transport));
        await device.InitializeAsync();
        await device.RunAsync(sp => sp.GetRequiredService<AddMedicine>().ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "tablet", 1m, 2, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            InitialQuantity: 60m), CancellationToken.None));
        await device.RunAsync(sp => sp.GetRequiredService<CreateSyncGroup>().ExecuteAsync(
            sp.GetRequiredService<ISyncTransport>(), Passphrase.ToCharArray(), Target, CancellationToken.None,
            SyncFileFormatTests.FastKdf));
        return device;
    }

    private async Task<SyncDevice> JoinAsync(SyncDevice via, string name, int checkpointEvery)
    {
        var path = Path.Combine(_root, $"{name}.db");
        var groupId = via.Settings.Load()!.GroupId;
        var joined = await via.RunAsync(sp => sp.GetRequiredService<JoinSyncGroup>().ExecuteAsync(
            Transport(via.Clock), groupId, Passphrase.ToCharArray(), path, Target, CancellationToken.None));
        joined.Settings.Provider.Should().Be(CloudProvider.OneDrive);
        joined.Settings.AccountId.Should().Be(Account);
        var device = Track(new SyncDevice(name, path, via.Clock.GetUtcNow(), joined.Settings, joined.Key,
            checkpointEvery, remote: Transport));
        await device.InitializeAsync();
        return device;
    }

    private SyncDevice Track(SyncDevice device)
    {
        _devices.Add(device);
        return device;
    }

    [Fact]
    public async Task Random_workloads_converge_through_the_app_folder()
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("SYNC_ONEDRIVE_SEEDS"), out var n) ? n : 3;
        var deletedTotal = 0;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var random = new Random(seed);
            var a = await CreateGroupAsync($"A{seed}", checkpointEvery: 15);
            var devices = new List<SyncDevice> { a, await JoinAsync(a, $"B{seed}", 15) };

            for (var step = 0; step < 40; step++)
            {
                var device = devices[random.Next(devices.Count)];
                if (random.Next(4) == 0)
                {
                    var result = await device.SyncAsync();
                    result.RebuildRequired.Should().BeFalse($"seed {seed}: {string.Join("; ", result.Problems)}");
                    result.Problems.Should().BeEmpty();
                    deletedTotal += result.SegmentsDeleted;
                }
                else
                {
                    await SyncConvergenceTests.ActAsync(device, random);
                }
                device.Clock.Advance(TimeSpan.FromMinutes(random.Next(1, 120)));
                if (step == 25)
                {
                    await device.SyncAsync();
                    devices.Add(await JoinAsync(device, $"C{seed}", 15));
                }
            }

            var end = devices.Max(d => d.Clock.GetUtcNow()).AddMinutes(1);
            for (var round = 0; round < 3; round++)
            {
                foreach (var d in devices)
                {
                    d.Clock.Set(end.AddMinutes(round));
                    (await d.SyncAsync()).Problems.Should().BeEmpty();
                }
            }
            var states = new List<string>();
            foreach (var d in devices)
            {
                d.Clock.Set(end.AddHours(1));
                await d.RunAsync(sp => sp.GetRequiredService<ConsumptionCatchUp>().RunAsync(CancellationToken.None));
                states.Add(await SyncStateDescriber.DescribeAsync(d));
            }
            foreach (var state in states.Skip(1))
            {
                var left = states[0].Split('\n').ToHashSet();
                var right = state.Split('\n').ToHashSet();
                _output.WriteLine(string.Join('\n', left.Except(right).Select(l => "- " + l)
                    .Concat(right.Except(left).Select(l => "+ " + l))));
            }
            states.Distinct().Should().ContainSingle($"seed {seed} must converge");
        }
        _output.WriteLine($"{deletedTotal} segments deleted by compaction.");
        // Guard against a vacuous run: compaction deleted through the API.
        deletedTotal.Should().BeGreaterThan(0);
        _drive.Files().Should().OnlyContain(f => f.StartsWith("sync/", StringComparison.Ordinal))
            .And.NotContain(f => f.Contains(OneDriveClient.TempPrefix, StringComparison.Ordinal));
    }
}
