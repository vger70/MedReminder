using System.Security.Cryptography;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Sync;
using MedReminder.Application.Sync.Remote;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace MedReminder.Infrastructure.Tests.Sync;

// B.1 Phase 4c end to end (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §6.1,
// §6.2): key rotation to remove a device, the rekey of the remaining
// devices with carry-over of their own operations, and pairing codes.
// Real SQLite databases synchronized through a folder.
public sealed class KeyRotationSyncTests : IDisposable
{
    private const string Passphrase = "sync passphrase for tests";
    private const string NewPassphrase = "a different passphrase";
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly ITestOutputHelper _output;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-rotation-" + Guid.NewGuid().ToString("N"));
    private readonly List<SyncDevice> _devices = new();
    private readonly ArchiveCipher _cipher = new();

    public KeyRotationSyncTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    private string Folder => Path.Combine(_root, "remote");

    private LocalFolderSyncTransport Transport => new(Folder);

    public void Dispose()
    {
        foreach (var d in _devices) d.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task A_rotation_removes_a_device_and_the_others_carry_their_operations_over()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");
        var lost = await JoinAsync(a, "Lost");
        var id = await MedicineIdAsync(a);
        await SyncAllAsync([a, b, lost]);

        // B records while A rotates: one change published but not yet
        // applied by A, one never published.
        await AddStockAsync(b, id, 7m, "published before the rotation");
        await b.SyncAsync();
        await AddStockAsync(lost, id, 1m, "from the lost device, before the rotation");
        await lost.SyncAsync();

        var rotated = await RotateAsync(a);
        rotated.KeyVersion.Should().Be(2);
        rotated.Generation.Should().Be(2);
        await AddStockAsync(a, id, 3m, "after the rotation");
        await a.SyncAsync();
        await AddStockAsync(b, id, 5m, "recorded offline");

        var seen = await b.SyncAsync();
        seen.NewKeyRequired.Should().BeTrue();
        seen.OperationsPublished.Should().Be(0, "nothing may be sealed with a key the lost device holds");
        (await lost.SyncAsync()).NewKeyRequired.Should().BeTrue();

        await FluentActions.Awaiting(() => RekeyAsync(lost, new SyncKeySource.Passphrase(Passphrase.ToCharArray())))
            .Should().ThrowAsync<CryptographicException>();
        var (b2, carried, dropped) = await RekeyAsync(b, new SyncKeySource.Passphrase(NewPassphrase.ToCharArray()));
        carried.Should().Be(2);
        dropped.Should().Be(0);

        await SyncAllAsync([a, b2]);
        ShouldConverge(await StatesAsync([a, b2]));
        var notes = (await a.RunAsync(sp => sp.GetRequiredService<IStockMovementRepository>().ListForMedicineAsync(id, default)))
            .Select(m => m.Notes).ToList();
        notes.Should().Contain(["published before the rotation", "recorded offline", "after the rotation"]);
        notes.Should().NotContain("from the lost device, before the rotation",
            "the rotating device had not applied it, and the lost device cannot bring it to the new generation");
    }

    [Fact]
    public async Task Nothing_written_after_a_rotation_opens_with_the_old_key()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");
        var groupId = a.Settings.Load()!.GroupId;
        var oldKey = a.Keys.Load(groupId, 1)!;

        await RotateAsync(a);
        var (b2, _, _) = await RekeyAsync(b, new SyncKeySource.Passphrase(NewPassphrase.ToCharArray()));
        var id = await MedicineIdAsync(a);
        await AddStockAsync(b2, id, 2m, "new generation");
        await SyncAllAsync([a, b2]);

        var files = Directory.EnumerateFiles(Path.Combine(Folder, groupId.ToString("N")), "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".mrs", StringComparison.Ordinal) || f.EndsWith(".mrg", StringComparison.Ordinal)
                || f.EndsWith(".mrd", StringComparison.Ordinal) || f.EndsWith(".mrc", StringComparison.Ordinal))
            .Select(f => File.ReadAllBytes(f))
            .Where(f => SyncFileCodec.ReadHeader(f).Generation == 2)
            .ToList();
        files.Should().HaveCountGreaterThan(2);
        foreach (var file in files)
        {
            FluentActions.Invoking(() => SyncFileCodec.Open(_cipher, oldKey, file)).Should().Throw<CryptographicException>();
        }
    }

    [Fact]
    public async Task A_new_device_joins_the_rotated_generation_with_the_new_passphrase_only()
    {
        var a = await CreateGroupAsync();
        await RotateAsync(a);
        var groupId = a.Settings.Load()!.GroupId;

        await FluentActions.Awaiting(() => a.RunAsync(sp => sp.GetRequiredService<JoinSyncGroup>().ExecuteAsync(
                Transport, groupId, Passphrase.ToCharArray(), Path.Combine(_root, "old.db"), SyncTarget.ForFolder(Folder),
                CancellationToken.None)))
            .Should().ThrowAsync<CryptographicException>();

        var c = await JoinAsync(a, "C", NewPassphrase);
        c.Settings.Load()!.KeyVersion.Should().Be(2);
        c.Settings.Load()!.Generation.Should().Be(2);
        await SyncAllAsync([a, c]);
        ShouldConverge(await StatesAsync([a, c]));
    }

    [Fact]
    public async Task A_generation_sealed_with_an_older_key_is_ignored()
    {
        var a = await CreateGroupAsync();
        var lost = await JoinAsync(a, "Lost");
        var id = await MedicineIdAsync(a);
        await RotateAsync(a);

        // The lost device still holds key 1 and write access to the
        // folder: it starts a generation of its own.
        var forged = await lost.RunAsync(sp => sp.GetRequiredService<ResetSyncGeneration>().ExecuteAsync(
            Transport, CancellationToken.None));
        forged.Generation.Should().Be(3);

        await AddStockAsync(a, id, 4m, "still synced");
        var result = await a.SyncAsync();

        result.NewerGeneration.Should().BeNull();
        result.NewKeyRequired.Should().BeFalse();
        result.OperationsPublished.Should().Be(1);
        result.Problems.Should().ContainSingle(p => p.Contains("Generation 3 is not sealed", StringComparison.Ordinal));

        var c = await JoinAsync(a, "C", NewPassphrase);
        c.Settings.Load()!.Generation.Should().Be(2);
        await SyncAllAsync([a, c]);
        ShouldConverge(await StatesAsync([a, c]));
    }

    [Fact]
    public async Task A_device_joins_with_a_pairing_code_while_the_offer_lasts()
    {
        var a = await CreateGroupAsync();
        var offer = await a.RunAsync(sp => sp.GetRequiredService<SyncPairingOffers>().StartAsync(Transport, CancellationToken.None));
        offer.ExpiresAt.Should().Be(Now + SyncPairingOffers.Lifetime);

        SyncPairingCode.TryParse(offer.Code.Text, out var scanned).Should().BeTrue();
        var d = await JoinWithCodeAsync(a, "D", scanned!);
        await SyncAllAsync([a, d]);
        ShouldConverge(await StatesAsync([a, d]));

        await a.RunAsync(sp => sp.GetRequiredService<SyncPairingOffers>().EndAsync(Transport, CancellationToken.None));
        await FluentActions.Awaiting(() => JoinWithCodeAsync(a, "E", scanned!))
            .Should().ThrowAsync<SyncPairingExpiredException>();
    }

    [Fact]
    public async Task An_expired_or_replaced_pairing_code_opens_nothing()
    {
        var a = await CreateGroupAsync();
        var first = await a.RunAsync(sp => sp.GetRequiredService<SyncPairingOffers>().StartAsync(Transport, CancellationToken.None));
        var second = await a.RunAsync(sp => sp.GetRequiredService<SyncPairingOffers>().StartAsync(Transport, CancellationToken.None));

        await FluentActions.Awaiting(() => JoinWithCodeAsync(a, "Old", first.Code))
            .Should().ThrowAsync<CryptographicException>("a new offer replaces the previous one");

        a.Clock.Advance(SyncPairingOffers.Lifetime + TimeSpan.FromSeconds(1));
        await FluentActions.Awaiting(() => JoinWithCodeAsync(a, "Late", second.Code))
            .Should().ThrowAsync<SyncPairingExpiredException>();
    }

    [Fact]
    public async Task A_device_left_behind_by_a_rotation_takes_the_new_key_from_a_pairing_code()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");
        var id = await MedicineIdAsync(a);
        await RotateAsync(a);
        await AddStockAsync(b, id, 6m, "offline on B");

        var offer = await a.RunAsync(sp => sp.GetRequiredService<SyncPairingOffers>().StartAsync(Transport, CancellationToken.None));
        var (b2, carried, _) = await RekeyAsync(b, new SyncKeySource.Pairing(offer.Code));

        carried.Should().Be(1);
        b2.Settings.Load()!.KeyVersion.Should().Be(2);
        b2.Settings.Load()!.DeviceId.Should().Be(b.Settings.Load()!.DeviceId);
        await SyncAllAsync([a, b2]);
        ShouldConverge(await StatesAsync([a, b2]));
    }

    [Fact]
    public async Task A_device_waiting_for_the_new_key_cannot_rotate_or_offer_pairing()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");
        await RotateAsync(a);

        await FluentActions.Awaiting(() => b.RunAsync(sp => sp.GetRequiredService<RotateSyncKey>().ExecuteAsync(
                Transport, "another passphrase".ToCharArray(), CancellationToken.None, SyncFileFormatTests.FastKdf)))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => b.RunAsync(sp => sp.GetRequiredService<SyncPairingOffers>().StartAsync(
                Transport, CancellationToken.None)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task The_devices_list_skips_records_of_the_previous_generation()
    {
        var a = await CreateGroupAsync();
        await JoinAsync(a, "B");
        await RotateAsync(a);

        var devices = await a.RunAsync(sp => sp.GetRequiredService<SyncEngine>().ListDevicesAsync(CancellationToken.None));

        devices.Select(d => d.Name).Should().Equal("A");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mrpair1.x")]
    [InlineData("mrpair2.00000000000000000000000000000001.00000000000000000000000000000002.folder.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mrpair1.00000000000000000000000000000001.00000000000000000000000000000002.Dropbox.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mrpair1.00000000000000000000000000000001.00000000000000000000000000000002.1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mrpair1.00000000000000000000000000000001.00000000000000000000000000000002.folder.AAAA")]
    public void A_malformed_pairing_code_is_refused(string? text)
        => SyncPairingCode.TryParse(text, out _).Should().BeFalse();

    [Fact]
    public void A_pairing_code_survives_copying_with_line_breaks()
    {
        var code = new SyncPairingCode(Guid.NewGuid(), Guid.NewGuid(), CloudProvider.GoogleDrive,
            RandomNumberGenerator.GetBytes(SyncPairingCode.SecretSize));
        var copied = code.Text.Insert(40, "\r\n ");

        SyncPairingCode.TryParse(copied, out var parsed).Should().BeTrue();

        parsed!.GroupId.Should().Be(code.GroupId);
        parsed.DeviceId.Should().Be(code.DeviceId);
        parsed.Provider.Should().Be(CloudProvider.GoogleDrive);
        parsed.Secret.Should().Equal(code.Secret);
        parsed.ToString().Should().NotContain(code.Text[^10..], "the secret must not reach a log line");
    }

    private async Task<SyncDevice> CreateGroupAsync()
    {
        var device = Track(new SyncDevice("A", Path.Combine(_root, "A.db"), Now, settings: null));
        await device.InitializeAsync();
        await device.RunAsync(sp => sp.GetRequiredService<AddMedicine>().ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "tablet", 1m, 2, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            InitialQuantity: 60m), CancellationToken.None));
        await device.RunAsync(sp => sp.GetRequiredService<CreateSyncGroup>().ExecuteAsync(
            Transport, Passphrase.ToCharArray(), SyncTarget.ForFolder(Folder), CancellationToken.None,
            SyncFileFormatTests.FastKdf));
        return device;
    }

    private async Task<SyncDevice> JoinAsync(SyncDevice via, string name, string passphrase = Passphrase)
    {
        var path = Path.Combine(_root, $"{name}.db");
        var joined = await via.RunAsync(sp => sp.GetRequiredService<JoinSyncGroup>().ExecuteAsync(
            Transport, via.Settings.Load()!.GroupId, passphrase.ToCharArray(), path, SyncTarget.ForFolder(Folder),
            CancellationToken.None));
        var device = Track(new SyncDevice(name, path, via.Clock.GetUtcNow(), joined.Settings, joined.Key));
        await device.InitializeAsync();
        return device;
    }

    private async Task<SyncDevice> JoinWithCodeAsync(SyncDevice via, string name, SyncPairingCode code)
    {
        var path = Path.Combine(_root, $"{name}.db");
        var joined = await via.RunAsync(sp => sp.GetRequiredService<JoinSyncGroup>().ExecuteAsync(
            Transport, code.GroupId, new SyncKeySource.Pairing(code), path, SyncTarget.ForFolder(Folder),
            CancellationToken.None));
        var device = Track(new SyncDevice(name, path, via.Clock.GetUtcNow(), joined.Settings, joined.Key));
        await device.InitializeAsync();
        return device;
    }

    private Task<SyncSettings> RotateAsync(SyncDevice device)
        => device.RunAsync(sp => sp.GetRequiredService<RotateSyncKey>().ExecuteAsync(
            Transport, NewPassphrase.ToCharArray(), CancellationToken.None, SyncFileFormatTests.FastKdf));

    // What SyncSetupService.RekeyAsync does on the desktop: read the own
    // operations, build the new database, then carry them over into it.
    private async Task<(SyncDevice Device, int Carried, int Dropped)> RekeyAsync(SyncDevice device, SyncKeySource source)
    {
        var current = device.Settings.Load()!;
        var own = (await device.RunAsync(sp => sp.GetRequiredService<ISyncOperationRepository>().ListAllAsync(default)))
            .Where(o => o.DeviceId == current.DeviceId && o.Generation == current.Generation)
            .ToList();
        var path = Path.Combine(_root, $"{device.Name}-{Guid.NewGuid():N}.db");
        var joined = await device.RunAsync(sp => sp.GetRequiredService<JoinSyncGroup>().RekeyAsync(
            Transport, current, source, path, CancellationToken.None));
        var rebuilt = Track(new SyncDevice(device.Name + "2", path, device.Clock.GetUtcNow(), joined.Settings, joined.Key));
        await rebuilt.InitializeAsync();
        var (carried, dropped) = await rebuilt.RunAsync(sp =>
            sp.GetRequiredService<ApplyRemoteOperations>().ApplyCarriedAsync(own, CancellationToken.None));
        return (rebuilt, carried, dropped);
    }

    private SyncDevice Track(SyncDevice device)
    {
        _devices.Add(device);
        return device;
    }

    private static async Task<Guid> MedicineIdAsync(SyncDevice device)
        => (await device.RunAsync(sp => sp.GetRequiredService<IMedicineRepository>().ListAllAsync(default)))[0].Id;

    private static Task AddStockAsync(SyncDevice device, Guid medicineId, decimal quantity, string notes)
        => device.RunAsync(sp => sp.GetRequiredService<AddStock>().ExecuteAsync(
            new AddStockCommand(medicineId, quantity, StockMovementKind.ManualAdd, notes), default));

    private static async Task SyncAllAsync(IReadOnlyList<SyncDevice> devices, int rounds = 3)
    {
        for (var round = 0; round < rounds; round++)
        {
            foreach (var d in devices)
            {
                var result = await d.SyncAsync();
                result.NewerGeneration.Should().BeNull(d.Name);
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
}
