using Android.App;
using Android.App.Usage;
using Android.Content;
using Android.OS;

namespace MedReminder.MobileSpikes.Spikes;

// Power and scheduling state that decides how late alarms and periodic
// work run (S5, S8).
internal static class DeviceState
{
    private static Context Ctx => global::Android.App.Application.Context;

    public static bool PowerSaveMode => Ctx.GetSystemService(Context.PowerService) is PowerManager pm && pm.IsPowerSaveMode;

    public static bool DeviceIdleMode => Ctx.GetSystemService(Context.PowerService) is PowerManager pm && pm.IsDeviceIdleMode;

    public static bool IgnoringBatteryOptimizations
        => Ctx.GetSystemService(Context.PowerService) is PowerManager pm && pm.IsIgnoringBatteryOptimizations(Ctx.PackageName);

    public static bool Charging => Ctx.GetSystemService(Context.BatteryService) is BatteryManager bm && bm.IsCharging;

    public static string StandbyBucket
    {
        get
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(28)
                || Ctx.GetSystemService(Context.UsageStatsService) is not UsageStatsManager usage)
            {
                return "n/a";
            }
            var bucket = (int)usage.AppStandbyBucket;
            return bucket switch
            {
                <= 5 => $"exempted({bucket})",
                <= 10 => "active",
                <= 20 => "working-set",
                <= 30 => "frequent",
                <= 40 => "rare",
                <= 45 => "restricted",
                _ => $"never({bucket})",
            };
        }
    }

    public static bool CanScheduleExactAlarms
        => !OperatingSystem.IsAndroidVersionAtLeast(31)
            || (Ctx.GetSystemService(Context.AlarmService) is AlarmManager am && am.CanScheduleExactAlarms());

    // Milliseconds since this process started: a few seconds means the
    // alarm or the worker started the process (app not running).
    public static long ProcessAgeMs => SystemClock.ElapsedRealtime() - global::Android.OS.Process.StartElapsedRealtime;
}
