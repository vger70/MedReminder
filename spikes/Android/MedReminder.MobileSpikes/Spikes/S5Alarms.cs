using System.Globalization;
using Android.App;
using Android.Content;
using AndroidX.Core.App;

namespace MedReminder.MobileSpikes.Spikes;

internal enum AlarmKind
{
    // AlarmManager.setExactAndAllowWhileIdle: needs SCHEDULE_EXACT_ALARM.
    Exact,

    // AlarmManager.setAlarmClock: exact, shown as the next alarm, also
    // needs SCHEDULE_EXACT_ALARM.
    AlarmClock,

    // AlarmManager.setAndAllowWhileIdle: inexact, the fallback when exact
    // alarms are denied (ANALYSIS-B1-MOBILE-SYNC.md §8.2).
    Inexact,

    // AlarmManager.setWindow with a 10-minute window.
    Window,
}

// S5 — local notifications on Android (§13 Phase 0, §8.1, §8.2): does a
// notification planned with AlarmManager fire on time with the app in
// the background, swiped away, force-stopped, after a reboot, under
// Doze, and with exact alarms denied? Each scheduled alarm is written to
// a plan file; the receiver logs when it really fired and posts a
// notification; a boot receiver re-plans the future alarms, as the
// Phase 5 client would.
internal static class S5Alarms
{
    public const string ChannelId = "s5";
    public const string PlanFile = "s5-plan.tsv";
    public const string EventsFile = "s5-events.tsv";
    private const string ExtraId = "s5.id";
    private const string ExtraKind = "s5.kind";
    private const string ExtraPlanned = "s5.planned";

    public static readonly string[] Scenarios =
        ["screen-off", "swiped", "force-stopped", "reboot", "exact-denied", "doze-overnight", "foreground"];

    private static readonly int[] ShortOffsetsMinutes = [2, 5, 10, 20];
    private static readonly int[] LongOffsetsMinutes = [30, 60, 120, 240, 480];

    private static Context Ctx => global::Android.App.Application.Context;

    public static string ScheduleBattery(string scenario, bool longBattery)
    {
        EnsureChannel();
        var now = SpikeFiles.NowMs();
        var nextId = SpikeFiles.Read(PlanFile).Select(p => int.Parse(p[0], CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max() + 1;
        int scheduled = 0, denied = 0;
        foreach (var offset in longBattery ? LongOffsetsMinutes : ShortOffsetsMinutes)
        {
            foreach (var kind in Enum.GetValues<AlarmKind>())
            {
                var id = nextId++;
                var planned = now + (offset * 60_000L);
                var status = Schedule(id, kind, planned);
                // id, kind, scenario, offset (min), scheduled at, planned at, status
                SpikeFiles.Append(PlanFile, id, kind, scenario, offset, now, planned, status);
                if (status == "scheduled") scheduled++; else denied++;
            }
        }
        return $"{scheduled} scheduled, {denied} denied; exact alarms allowed: {DeviceState.CanScheduleExactAlarms}";
    }

    private static string Schedule(int id, AlarmKind kind, long plannedUtcMs)
    {
        if (Ctx.GetSystemService(Context.AlarmService) is not AlarmManager alarms)
        {
            return "denied:no-alarm-service";
        }
        var pending = PendingFor(id, kind, plannedUtcMs);
        try
        {
            switch (kind)
            {
                case AlarmKind.Exact:
                    alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, plannedUtcMs, pending);
                    break;
                case AlarmKind.AlarmClock:
                    var launch = Ctx.PackageManager!.GetLaunchIntentForPackage(Ctx.PackageName!)!;
                    var show = PendingIntent.GetActivity(Ctx, id, launch, PendingIntentFlags.Immutable)!;
                    alarms.SetAlarmClock(new AlarmManager.AlarmClockInfo(plannedUtcMs, show), pending);
                    break;
                case AlarmKind.Inexact:
                    alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, plannedUtcMs, pending);
                    break;
                case AlarmKind.Window:
                    alarms.SetWindow(AlarmType.RtcWakeup, plannedUtcMs, 10 * 60_000L, pending);
                    break;
            }
            return "scheduled";
        }
        catch (Java.Lang.SecurityException ex)
        {
            return "denied:" + ex.GetType().Name;
        }
    }

    private static PendingIntent PendingFor(int id, AlarmKind kind, long plannedUtcMs)
    {
        var intent = new Intent(Ctx, typeof(S5AlarmReceiver));
        intent.PutExtra(ExtraId, id);
        intent.PutExtra(ExtraKind, kind.ToString());
        intent.PutExtra(ExtraPlanned, plannedUtcMs);
        return PendingIntent.GetBroadcast(Ctx, id, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    public static void OnFired(Intent intent)
    {
        var now = SpikeFiles.NowMs();
        var id = intent.GetIntExtra(ExtraId, 0);
        var kind = intent.GetStringExtra(ExtraKind) ?? "?";
        var planned = intent.GetLongExtra(ExtraPlanned, 0);
        // type, id, kind, planned at, fired at, process age, power save, device idle
        SpikeFiles.Append(EventsFile, "fired", id, kind, planned, now, DeviceState.ProcessAgeMs, DeviceState.PowerSaveMode, DeviceState.DeviceIdleMode);

        EnsureChannel();
        var lateSeconds = (now - planned) / 1000.0;
        var notification = new NotificationCompat.Builder(Ctx, ChannelId)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)!
            .SetContentTitle($"S5 {kind} #{id}")!
            .SetContentText(string.Create(CultureInfo.InvariantCulture, $"planned {SpikeFiles.Local(planned)}, late {lateSeconds:0} s"))!
            .SetAutoCancel(true)!
            .Build()!;
        try
        {
            NotificationManagerCompat.From(Ctx)!.Notify(id, notification);
        }
        catch (Java.Lang.SecurityException)
        {
            // POST_NOTIFICATIONS not granted: the fired event is logged anyway.
        }
    }

    // BOOT_COMPLETED: alarms are cleared on reboot (§8.2). Re-plan the
    // future ones and record the ones whose time passed while the phone
    // was off.
    public static void OnBoot()
    {
        var now = SpikeFiles.NowMs();
        var fired = FiredIds();
        int rescheduled = 0, missed = 0;
        foreach (var p in SpikeFiles.Read(PlanFile).Where(p => p[6] == "scheduled"))
        {
            var id = int.Parse(p[0], CultureInfo.InvariantCulture);
            if (fired.Contains(id)) continue;
            var planned = long.Parse(p[5], CultureInfo.InvariantCulture);
            if (planned > now)
            {
                var status = Schedule(id, Enum.Parse<AlarmKind>(p[1]), planned);
                SpikeFiles.Append(EventsFile, "rescheduled", id, p[1], planned, now, status, string.Empty, string.Empty);
                rescheduled++;
            }
            else
            {
                SpikeFiles.Append(EventsFile, "missed-at-boot", id, p[1], planned, now, string.Empty, string.Empty, string.Empty);
                missed++;
            }
        }
        SpikeFiles.Append(EventsFile, "boot", 0, string.Empty, 0, now, rescheduled, missed, DeviceState.CanScheduleExactAlarms);
    }

    public static void Clear()
    {
        if (Ctx.GetSystemService(Context.AlarmService) is AlarmManager alarms)
        {
            foreach (var p in SpikeFiles.Read(PlanFile))
            {
                var id = int.Parse(p[0], CultureInfo.InvariantCulture);
                var intent = new Intent(Ctx, typeof(S5AlarmReceiver));
                var pending = PendingIntent.GetBroadcast(Ctx, id, intent, PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
                if (pending is not null)
                {
                    alarms.Cancel(pending);
                }
            }
        }
        SpikeFiles.Delete(PlanFile);
        SpikeFiles.Delete(EventsFile);
    }

    public static void Collect(SpikeReport report)
    {
        const string Spike = "S5";
        var enabled = NotificationManagerCompat.From(Ctx)!.AreNotificationsEnabled();
        report.Add(Spike, "Notifications enabled", enabled ? Outcome.Pass : Outcome.Fail, enabled.ToString());
        report.Add(Spike, "Exact alarms allowed (SCHEDULE_EXACT_ALARM)", Outcome.Measured, DeviceState.CanScheduleExactAlarms.ToString());
        report.Add(Spike, "Battery optimization", Outcome.Measured,
            $"ignoring optimizations {DeviceState.IgnoringBatteryOptimizations}; standby bucket {DeviceState.StandbyBucket}; power save {DeviceState.PowerSaveMode}");

        var events = SpikeFiles.Read(EventsFile);
        var firedById = events.Where(e => e[0] == "fired")
            .GroupBy(e => int.Parse(e[1], CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.First());
        var rescheduledIds = events.Where(e => e[0] == "rescheduled").Select(e => int.Parse(e[1], CultureInfo.InvariantCulture)).ToHashSet();
        var missedIds = events.Where(e => e[0] == "missed-at-boot").Select(e => int.Parse(e[1], CultureInfo.InvariantCulture)).ToHashSet();
        var now = SpikeFiles.NowMs();

        foreach (var p in SpikeFiles.Read(PlanFile))
        {
            var id = int.Parse(p[0], CultureInfo.InvariantCulture);
            var kind = Enum.Parse<AlarmKind>(p[1]);
            var planned = long.Parse(p[5], CultureInfo.InvariantCulture);
            var check = $"{p[2]} {kind} +{p[3]} min (#{id}, planned {SpikeFiles.Local(planned)})";
            var notes = (rescheduledIds.Contains(id) ? "; re-planned at boot" : string.Empty)
                + (missedIds.Contains(id) ? "; time passed while the phone was off" : string.Empty);

            if (p[6] != "scheduled")
            {
                report.Add(Spike, check, Outcome.Measured, p[6] + notes);
            }
            else if (firedById.TryGetValue(id, out var e))
            {
                var late = (long.Parse(e[4], CultureInfo.InvariantCulture) - planned) / 1000.0;
                var cold = long.Parse(e[5], CultureInfo.InvariantCulture) < 5000 ? "; started the process" : string.Empty;
                var detail = string.Create(CultureInfo.InvariantCulture,
                    $"fired {late:0} s late; power save {e[6]}, idle {e[7]}{cold}{notes}");
                var exact = kind is AlarmKind.Exact or AlarmKind.AlarmClock;
                report.Add(Spike, check, !exact ? Outcome.Measured : late <= 60 ? Outcome.Pass : Outcome.Fail, detail);
            }
            else if (planned + (15 * 60_000L) < now)
            {
                report.Add(Spike, check, Outcome.Fail, "not fired" + notes);
            }
            else
            {
                report.Add(Spike, check, Outcome.Skipped, "pending" + notes);
            }
        }

        foreach (var b in events.Where(e => e[0] == "boot"))
        {
            report.Add(Spike, "Boot", Outcome.Measured,
                $"at {SpikeFiles.Local(long.Parse(b[4], CultureInfo.InvariantCulture))}: {b[5]} alarm(s) re-planned, {b[6]} passed while off; exact alarms allowed {b[7]}");
        }
    }

    private static HashSet<int> FiredIds()
        => [.. SpikeFiles.Read(EventsFile).Where(e => e[0] == "fired").Select(e => int.Parse(e[1], CultureInfo.InvariantCulture))];

    private static void EnsureChannel()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26)
            && Ctx.GetSystemService(Context.NotificationService) is NotificationManager manager)
        {
            manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "S5 spike", NotificationImportance.High));
        }
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class S5AlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent is not null)
        {
            S5Alarms.OnFired(intent);
        }
    }
}

// Exported so the system can deliver BOOT_COMPLETED; it acts on that
// action only.
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public sealed class S5BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action == Intent.ActionBootCompleted)
        {
            S5Alarms.OnBoot();
        }
    }
}
