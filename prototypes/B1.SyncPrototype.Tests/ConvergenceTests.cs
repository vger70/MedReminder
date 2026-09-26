using FluentAssertions;
using MedReminder.Prototypes.Sync.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace MedReminder.Prototypes.Sync.Tests;

// S9 acceptance (b). Seed count from S9_SIM_SEEDS (default 300 for a
// quick run; the recorded result used 10 000).
public sealed class ConvergenceTests(ITestOutputHelper output)
{
    private static int Seeds => int.TryParse(Environment.GetEnvironmentVariable("S9_SIM_SEEDS"), out var n) ? n : 300;

    private static int FirstSeed => int.TryParse(Environment.GetEnvironmentVariable("S9_SIM_FIRST_SEED"), out var f) ? f : 1;

    [Fact]
    public void Devices_converge_on_random_histories()
    {
        var failures = new List<string>();
        var totals = new long[7];
        for (var seed = FirstSeed; seed < FirstSeed + Seeds; seed++)
        {
            var result = new SyncSimulation(seed).Run();
            totals[0] += result.Devices;
            totals[1] += result.Operations;
            totals[2] += result.Medicines;
            totals[3] += result.Bootstraps;
            totals[4] += result.SegmentsDeleted;
            totals[5] += result.DuplicatesIgnored;
            totals[6] += result.ScheduleConflicts;
            if (result.Failures.Count > 0)
            {
                failures.Add($"seed {seed}: " + string.Join("; ", result.Failures.Take(5)));
                if (failures.Count >= 5) break;
            }
        }
        output.WriteLine(
            $"seeds={Seeds} devices={totals[0]} operations={totals[1]} medicines={totals[2]} " +
            $"bootstraps={totals[3]} segmentsDeleted={totals[4]} duplicatesIgnored={totals[5]} scheduleConflicts={totals[6]}");
        failures.Should().BeEmpty();
    }

    [Fact]
    public void Hlc_orders_by_physical_then_counter_then_device()
    {
        var a = new Hlc(10, 0, Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var b = new Hlc(10, 1, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var c = new Hlc(11, 0, Guid.Empty);
        (a < b).Should().BeTrue();
        (b < c).Should().BeTrue();
        new Hlc(10, 0, Guid.Empty).CompareTo(a).Should().BeNegative();
    }

    [Fact]
    public void Clock_never_goes_behind_an_observed_timestamp()
    {
        long now = 1_000;
        var clock = new HlcClock(Guid.NewGuid(), () => now);
        var remote = new Hlc(5_000, 7, Guid.NewGuid());
        clock.Observe(remote);
        (clock.Now() > remote).Should().BeTrue();
    }
}
