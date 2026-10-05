using System.Globalization;
using Android.Content;
using AndroidX.Work;

namespace MedReminder.MobileSpikes.Spikes;

// S8 — background sync cadence (§13 Phase 0, §7.4): how often does
// WorkManager run a 15-minute periodic job with a network constraint,
// with and without battery saver, as the phone moves between standby
// buckets and Doze? The worker only records its run: no network call,
// so the figures show the scheduling the Phase 5 sync would get.
internal static class S8Background
{
    public const string RunsFile = "s8-runs.tsv";
    public const string StateFile = "s8-state.tsv";
    private const string WorkName = "s8-probe";

    private static Context Ctx => global::Android.App.Application.Context;

    public static string Start()
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)
            .Build();
        var request = (PeriodicWorkRequest)new PeriodicWorkRequest.Builder(
                Java.Lang.Class.FromType(typeof(S8ProbeWorker)), 15, Java.Util.Concurrent.TimeUnit.Minutes!)
            .SetConstraints(constraints)
            .Build();
        WorkManager.GetInstance(Ctx).EnqueueUniquePeriodicWork(WorkName, ExistingPeriodicWorkPolicy.Keep!, request);
        SpikeFiles.Append(StateFile, "started", SpikeFiles.NowMs(), DeviceState.PowerSaveMode, DeviceState.IgnoringBatteryOptimizations);
        return "periodic work enqueued (15 min, network connected)";
    }

    public static void Stop()
    {
        WorkManager.GetInstance(Ctx).CancelUniqueWork(WorkName);
        SpikeFiles.Append(StateFile, "stopped", SpikeFiles.NowMs(), DeviceState.PowerSaveMode, DeviceState.IgnoringBatteryOptimizations);
    }

    public static void Clear()
    {
        WorkManager.GetInstance(Ctx).CancelUniqueWork(WorkName);
        SpikeFiles.Delete(RunsFile);
        SpikeFiles.Delete(StateFile);
    }

    public static void RecordRun(int attempt)
        // run at, power save, device idle, standby bucket, charging, process age, attempt
        => SpikeFiles.Append(RunsFile, SpikeFiles.NowMs(), DeviceState.PowerSaveMode, DeviceState.DeviceIdleMode,
            DeviceState.StandbyBucket, DeviceState.Charging, DeviceState.ProcessAgeMs, attempt);

    public static void Collect(SpikeReport report)
    {
        const string Spike = "S8";
        var state = SpikeFiles.Read(StateFile);
        foreach (var s in state)
        {
            report.Add(Spike, $"Work {s[0]}", Outcome.Measured,
                $"at {SpikeFiles.Local(long.Parse(s[1], CultureInfo.InvariantCulture))}; power save {s[2]}; ignoring battery optimizations {s[3]}");
        }

        var runs = SpikeFiles.Read(RunsFile)
            .Select(r => (At: long.Parse(r[0], CultureInfo.InvariantCulture), PowerSave: r[1] == "True", Idle: r[2] == "True", Bucket: r[3], Charging: r[4] == "True", Cold: long.Parse(r[5], CultureInfo.InvariantCulture) < 5000))
            .OrderBy(r => r.At)
            .ToList();
        report.Add(Spike, "Periodic work ran", runs.Count >= 2 ? Outcome.Pass : Outcome.Fail,
            runs.Count == 0 ? "no run recorded" : $"{runs.Count} run(s), {SpikeFiles.Local(runs[0].At)} to {SpikeFiles.Local(runs[^1].At)}; {runs.Count(r => r.Cold)} started the process");
        if (runs.Count < 2)
        {
            return;
        }

        var gaps = runs.Zip(runs.Skip(1), (a, b) => (Minutes: (b.At - a.At) / 60_000.0, b.PowerSave, b.Charging, b.Bucket)).ToList();
        AddGaps(report, Spike, "Gap between runs, all", gaps.Select(g => g.Minutes));
        AddGaps(report, Spike, "Gap between runs, battery saver off, on battery", gaps.Where(g => !g.PowerSave && !g.Charging).Select(g => g.Minutes));
        AddGaps(report, Spike, "Gap between runs, battery saver on", gaps.Where(g => g.PowerSave).Select(g => g.Minutes));
        AddGaps(report, Spike, "Gap between runs, charging", gaps.Where(g => g.Charging).Select(g => g.Minutes));
        report.Add(Spike, "Standby buckets seen", Outcome.Measured,
            string.Join(", ", runs.GroupBy(r => r.Bucket).Select(g => $"{g.Key} x{g.Count()}")));
    }

    private static void AddGaps(SpikeReport report, string spike, string check, IEnumerable<double> minutes)
    {
        var list = minutes.OrderBy(m => m).ToList();
        if (list.Count == 0)
        {
            report.Add(spike, check, Outcome.Skipped, "no gap in this state");
            return;
        }
        var median = list[list.Count / 2];
        report.Add(spike, check, Outcome.Measured, string.Create(CultureInfo.InvariantCulture,
            $"{list.Count} gap(s): min {list[0]:0.0} min, median {median:0.0} min, max {list[^1]:0.0} min"));
    }
}

public sealed class S8ProbeWorker : Worker
{
    public S8ProbeWorker(Context context, WorkerParameters workerParams)
        : base(context, workerParams)
    {
    }

    public override Result DoWork()
    {
        S8Background.RecordRun(RunAttemptCount);
        return Result.InvokeSuccess()!;
    }
}
