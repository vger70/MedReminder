using System.Globalization;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Ledger;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Sync;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace MedReminder.Infrastructure.Tests.Sync;

// B.1 Phase 3b-1 convergence harness (docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §11, R2, R3, R6), on real SQLite databases.
//
// Three devices start from the same genesis (a copy of one database
// with sync enabled), run random user actions through the real use
// cases with skewed clocks, and exchange their logs at random, each
// pull applying the other device's log in HLC order (causal: HLC
// respects causality, and a device's log includes what it applied).
// After a full exchange every device must hold the same replicated
// state, the same register conflicts, and the same derived ledger.
//
// Since Phase 3b-2 stock counts are evaluated again on the facts
// recorded before them by HLC, so the equal derived stock also checks
// that every device builds the same snapshots. The genesis carries the
// genesis register versions (SyncGenesis).
// The seed count is SYNC_CONVERGENCE_SEEDS (default 40).
public sealed class SyncConvergenceTests : IDisposable
{
    private static readonly DateOnly Start = new(2026, 9, 1);
    private static readonly DateTimeOffset Genesis = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly ITestOutputHelper _output;
    private int _conflicts;
    private int _retractions;
    private int _reevaluated;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-conv-" + Guid.NewGuid().ToString("N"));

    public SyncConvergenceTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Random_workloads_converge()
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("SYNC_CONVERGENCE_SEEDS"), out var n) ? n : 40;
        var steps = 0;
        for (var seed = 1; seed <= seeds; seed++)
        {
            steps += await RunScenarioAsync(seed);
        }
        _output.WriteLine($"{seeds} scenarios, {steps} user actions applied, " +
            $"{_conflicts} register conflicts, {_retractions} retractions in the converged states, " +
            $"{_reevaluated} counts whose outcome the merge changed.");
        // Guard against a vacuous run: the workload must reach the merge
        // paths it is meant to test.
        if (seeds >= 20)
        {
            _conflicts.Should().BeGreaterThan(0);
            _retractions.Should().BeGreaterThan(0);
            _reevaluated.Should().BeGreaterThan(0);
        }
    }

    private async Task<int> RunScenarioAsync(int seed)
    {
        var random = new Random(seed);
        var devices = await CreateDevicesAsync(seed, count: 3);
        try
        {
            var actions = 0;
            for (var step = 0; step < 60; step++)
            {
                var device = devices[random.Next(devices.Count)];
                var roll = random.Next(100);
                if (roll < 20)
                {
                    var other = devices.Where(d => d != device).ElementAt(random.Next(devices.Count - 1));
                    await PullAsync(other, device);
                }
                else if (roll < 25)
                {
                    var minutes = random.Next(1, 600);
                    foreach (var d in devices) d.Clock.Advance(TimeSpan.FromMinutes(minutes));
                }
                else if (await ActAsync(device, random))
                {
                    actions++;
                }
                device.Clock.Advance(TimeSpan.FromSeconds(random.Next(1, 90)));
            }

            // Full exchange, twice, so every operation reaches everyone.
            for (var round = 0; round < 2; round++)
            {
                foreach (var to in devices)
                foreach (var from in devices.Where(d => d != to))
                {
                    await PullAsync(from, to);
                }
            }

            // Same date everywhere, then derive (§4.3: derived rows depend
            // on the local date).
            var end = devices.Max(d => d.Clock.GetUtcNow()).AddMinutes(1);
            var states = new List<string>();
            foreach (var d in devices)
            {
                d.Clock.Set(end);
                await d.RunAsync(sp => sp.GetRequiredService<ConsumptionCatchUp>().RunAsync(CancellationToken.None));
                states.Add(await SyncStateDescriber.DescribeAsync(d));
            }

            for (var i = 1; i < states.Count; i++)
            {
                if (states[i] != states[0])
                {
                    _output.WriteLine(Diff(states[0], states[i]));
                    if (Environment.GetEnvironmentVariable("SYNC_CONVERGENCE_DUMP") is { Length: > 0 } dump)
                    {
                        File.WriteAllText(Path.Combine(dump, $"seed{seed}-{devices[0].Name}.txt"), states[0]);
                        File.WriteAllText(Path.Combine(dump, $"seed{seed}-{devices[i].Name}.txt"), states[i]);
                    }
                }
                states[i].Should().Be(states[0], $"seed {seed}: {devices[i].Name} must converge with {devices[0].Name}");
            }
            _reevaluated += await CountChangedOutcomesAsync(devices[0]);
            _conflicts += states[0].Split('\n').Count(l => l.StartsWith("X ", StringComparison.Ordinal));
            _retractions += states[0].Split('\n').Count(l => l.StartsWith("R ", StringComparison.Ordinal));
            return actions;
        }
        finally
        {
            foreach (var d in devices) d.Dispose();
        }
    }

    private async Task<List<SyncDevice>> CreateDevicesAsync(int seed, int count)
    {
        var dir = Path.Combine(_root, seed.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        var group = new SyncSettings(Guid.NewGuid(), Guid.NewGuid(), 1);

        // Genesis: one medicine written before sync was enabled, so it has
        // no operation and no register version on any device.
        var genesisPath = Path.Combine(dir, "genesis.db");
        using (var first = new SyncDevice("genesis", genesisPath, Genesis, settings: null))
        {
            await first.InitializeAsync();
            await first.RunAsync(sp => sp.GetRequiredService<AddMedicine>().ExecuteAsync(new AddMedicineCommand(
                "Enalapril", "tablet", 1m, 2, Start, 7, NotificationChannels.Windows,
                Notes: "genesis", InitialQuantity: 60m,
                AdministrationSlots: [new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
                    new AdministrationSlotInput(1m, new TimeOnly(20, 0), null)]), CancellationToken.None));
            await first.RunAsync(sp => sp.GetRequiredService<SyncGenesis>().RecordAsync(CancellationToken.None));
        }
        SqliteConnection.ClearAllPools();

        var devices = new List<SyncDevice>();
        for (var i = 0; i < count; i++)
        {
            var path = Path.Combine(dir, $"device{i}.db");
            File.Copy(genesisPath, path);
            // Skewed clocks: up to two minutes apart.
            var device = new SyncDevice($"device{i}", path, Genesis.AddSeconds(i * 47),
                group with { DeviceId = Guid.NewGuid() });
            await device.InitializeAsync();
            devices.Add(device);
        }
        return devices;
    }

    private static async Task PullAsync(SyncDevice from, SyncDevice to)
    {
        var log = await from.RunAsync(sp => sp.GetRequiredService<ISyncOperationRepository>().ListAllAsync(CancellationToken.None));
        var result = await to.RunAsync(sp => sp.GetRequiredService<ApplyRemoteOperations>().ExecuteAsync(log, CancellationToken.None));
        result.Blocked.Should().BeNull();
    }

    // Counts whose re-evaluated correction differs from the one stored
    // on the device that recorded them.
    private static Task<int> CountChangedOutcomesAsync(SyncDevice device)
        => device.RunAsync(async sp =>
        {
            var changed = 0;
            var ledger = sp.GetRequiredService<LedgerSynchronizer>();
            var counts = sp.GetRequiredService<IStockCountRepository>();
            foreach (var medicine in await sp.GetRequiredService<IMedicineRepository>().ListAllAsync(default))
            {
                var stored = (await counts.ListForMedicineAsync(medicine.Id, default)).ToDictionary(c => c.Id);
                var facts = await ledger.LoadFactsAsync(medicine, default);
                changed += facts.Counts.Count(c => stored[c.Id].Correction != c.Correction);
            }
            return changed;
        });

    private static string Diff(string expected, string actual)
    {
        var left = expected.Split('\n').ToHashSet();
        var right = actual.Split('\n').ToHashSet();
        return string.Join('\n', left.Except(right).Select(l => "- " + l).Concat(right.Except(left).Select(l => "+ " + l)));
    }

    // One random user action. Returns false when the use case refused it
    // (the same validation a user would meet).
    internal static async Task<bool> ActAsync(SyncDevice device, Random random)
    {
        var medicines = await device.RunAsync(sp => sp.GetRequiredService<IMedicineRepository>().ListAllAsync(CancellationToken.None));
        var medicine = medicines[random.Next(medicines.Count)];
        var id = medicine.Id;
        var today = DateOnly.FromDateTime(device.Clock.GetUtcNow().UtcDateTime);
        var recentDay = today.AddDays(-random.Next(0, 4));
        if (recentDay < medicine.StartDate) recentDay = medicine.StartDate;

        try
        {
            switch (random.Next(13))
            {
                case 0:
                    await device.RunAsync(sp => sp.GetRequiredService<AddStock>().ExecuteAsync(
                        new AddStockCommand(id, random.Next(1, 40), (StockMovementKind)random.Next(2, 4)), default));
                    break;
                case 1:
                    await device.RunAsync(sp => sp.GetRequiredService<AdjustStockDown>().ExecuteAsync(
                        new AdjustStockDownCommand(id, random.Next(1, 5)), default));
                    break;
                case 2:
                case 3:
                    await device.RunAsync(sp => sp.GetRequiredService<RegisterIntake>().ExecuteAsync(
                        new RegisterIntakeCommand(id, recentDay,
                            random.Next(4) == 0 ? IntakeStatus.Skipped : IntakeStatus.Taken, random.Next(1, 3)), default));
                    break;
                case 4:
                    await device.RunAsync(sp => sp.GetRequiredService<ReconcileStock>().ExecuteAsync(
                        new ReconcileStockCommand(id, random.Next(0, 80), random.Next(0, 3)), default));
                    break;
                case 5:
                    await device.RunAsync(sp => sp.GetRequiredService<SuspendMedication>().ExecuteAsync(
                        new SuspendMedicationCommand(id, today.AddDays(random.Next(-3, 3))), default));
                    break;
                case 6:
                    await device.RunAsync(sp => sp.GetRequiredService<ResumeMedication>().ExecuteAsync(
                        new ResumeMedicationCommand(id, today.AddDays(random.Next(0, 3))), default));
                    break;
                case 7:
                    await device.RunAsync(sp => sp.GetRequiredService<ChangeMedicationSchedule>().ExecuteAsync(
                        new ChangeMedicationScheduleCommand(id, random.Next(1, 4), random.Next(1, 3),
                            today.AddDays(random.Next(0, 3))), default));
                    break;
                case 8:
                case 9:
                    await EditAsync(device, medicine, random);
                    break;
                case 10:
                    await device.RunAsync(sp => sp.GetRequiredService<DeactivateMedicine>().ExecuteAsync(
                        new DeactivateMedicineCommand(id), default));
                    break;
                case 11:
                    var history = await device.RunAsync(sp => sp.GetRequiredService<FactHistoryQuery>().LoadAsync(id, default));
                    var candidates = history.Where(h => h.CanRetract).ToList();
                    if (candidates.Count == 0) return false;
                    var pick = candidates[random.Next(candidates.Count)];
                    await device.RunAsync(sp => sp.GetRequiredService<RetractFact>().ExecuteAsync(
                        new RetractFactCommand(id, pick.Kind, pick.FactId), default));
                    break;
                default:
                    await device.RunAsync(sp => sp.GetRequiredService<AddMedicine>().ExecuteAsync(new AddMedicineCommand(
                        $"Medicine {random.Next(1000)}", "tablet", 1m, 1, today.AddDays(-random.Next(0, 5)), 5,
                        NotificationChannels.Windows, InitialQuantity: random.Next(0, 50)), default));
                    break;
            }
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    // The edit dialog path: the stored values as baseline, a few changes.
    private static async Task EditAsync(SyncDevice device, Medicine medicine, Random random)
    {
        var slots = await device.RunAsync(sp => sp.GetRequiredService<IMedicationAdministrationSlotRepository>()
            .ListForMedicineAsync(medicine.Id, default));
        var baseline = new UpdateMedicineCommand(
            medicine.Id, medicine.Name, medicine.ActiveIngredient, medicine.Package, medicine.Unit,
            medicine.ThresholdDays, medicine.NotificationChannels, medicine.EndDate, medicine.DoctorName,
            medicine.Notes, medicine.IsActive, medicine.RemindOnDose,
            [.. slots.Select(s => new AdministrationSlotInput(s.Dose, s.Time, s.TimingLabel))]);

        var edit = baseline;
        switch (random.Next(5))
        {
            case 0: edit = edit with { Notes = $"note {random.Next(100)}" }; break;
            case 1: edit = edit with { ThresholdDays = random.Next(1, 15) }; break;
            case 2: edit = edit with { IsActive = !medicine.IsActive }; break;
            case 3:
                edit = edit with
                {
                    AdministrationSlots = [.. Enumerable.Range(0, random.Next(0, 3))
                        .Select(i => new AdministrationSlotInput(random.Next(1, 3), new TimeOnly(8 + i * 6, 0), null))],
                };
                break;
            default: edit = edit with { DoctorName = $"Dr {random.Next(10)}", Notes = null }; break;
        }

        await device.RunAsync(sp => sp.GetRequiredService<UpdateMedicine>().ExecuteAsync(
            edit with { Baseline = baseline }, default));
    }
}
