using System.Diagnostics;
using System.Globalization;
using MedReminder.Application.Export;

namespace MedReminder.MobileSpikes.Spikes;

// S2 — Argon2id cost with Argon2Params.Default (t=3, m=64 MiB, p=1) on
// the phone (§13 Phase 0): target under 5 s per derivation, no
// out-of-memory. Three runs through the production IArchiveCipher; the
// first also checks the result against the reference.
internal static class S2Argon2Cost
{
    private const string Spike = "S2";
    private const int Runs = 3;
    private const double TargetMs = 5000;

    public static void Run(SpikeReport report, IArchiveCipher cipher)
    {
        var parameters = Argon2Params.Default;
        var timings = new List<double>();

        for (var run = 1; run <= Runs; run++)
        {
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
            var watch = Stopwatch.StartNew();
            try
            {
                var key = cipher.DeriveKey(KnownAnswers.Passphrase.ToCharArray(), KnownAnswers.Salt, parameters);
                watch.Stop();
                timings.Add(watch.Elapsed.TotalMilliseconds);

                var allocatedMiB = (GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore) / (1024.0 * 1024.0);
                var detail = string.Create(CultureInfo.InvariantCulture,
                    $"t={parameters.Iterations} m={parameters.MemoryKiB} KiB p={parameters.Parallelism}; managed allocation {allocatedMiB:0.0} MiB");
                if (run == 1)
                {
                    var ok = Convert.ToHexStringLower(key) == KnownAnswers.Argon2idDefaultHex;
                    detail += ok ? "; key equals the reference" : "; KEY DIFFERS FROM THE REFERENCE";
                    report.Add(Spike, $"Derivation {run}/{Runs}", ok ? Outcome.Measured : Outcome.Fail, detail, watch.Elapsed.TotalMilliseconds);
                }
                else
                {
                    report.Add(Spike, $"Derivation {run}/{Runs}", Outcome.Measured, detail, watch.Elapsed.TotalMilliseconds);
                }
            }
            // OutOfMemoryException included: it is the failure S2 looks for.
            catch (Exception ex)
            {
                report.Add(Spike, $"Derivation {run}/{Runs}", Outcome.Fail, SpikeRunner.Describe(ex), watch.Elapsed.TotalMilliseconds);
                return;
            }
        }

        var max = timings.Max();
        report.Add(Spike, "Worst derivation under 5 s", max < TargetMs ? Outcome.Pass : Outcome.Fail,
            string.Create(CultureInfo.InvariantCulture,
                $"min {timings.Min():0} ms, max {max:0} ms, target < {TargetMs:0} ms; peak working set {PeakWorkingSetMiB()}"));
    }

    // Process data comes from /proc on Android; report its absence
    // instead of failing the spike.
    private static string PeakWorkingSetMiB()
    {
        try
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)} MiB");
        }
        catch (Exception ex)
        {
            return $"unavailable ({ex.GetType().Name})";
        }
    }
}
