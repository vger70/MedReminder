using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Android.App;
using Android.Content;
using Android.OS;

namespace MedReminder.MobileSpikes.Spikes;

// Device, runtime and build facts for the report header. No account,
// serial number or other identifier of the phone or its owner.
internal static class EnvironmentInfo
{
    public static IReadOnlyDictionary<string, string> Collect()
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["device.manufacturer"] = Build.Manufacturer ?? string.Empty,
            ["device.model"] = Build.Model ?? string.Empty,
            ["android.release"] = Build.VERSION.Release ?? string.Empty,
            ["android.sdkInt"] = ((int)Build.VERSION.SdkInt).ToString(CultureInfo.InvariantCulture),
            ["android.abis"] = string.Join(",", Build.SupportedAbis ?? Array.Empty<string>()),
            ["dotnet.framework"] = RuntimeInformation.FrameworkDescription,
            ["dotnet.rid"] = RuntimeInformation.RuntimeIdentifier,
            ["dotnet.processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["runtime.isDynamicCodeSupported"] = RuntimeFeature.IsDynamicCodeSupported.ToString(),
            ["runtime.isDynamicCodeCompiled"] = RuntimeFeature.IsDynamicCodeCompiled.ToString(),
            ["gc.totalAvailableMiB"] = (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture),
            ["appContext.jsonReflectionEnabledByDefault"] =
                AppContext.TryGetSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", out var enabled)
                    ? enabled.ToString()
                    : "unset",
        };

        if (global::Android.App.Application.Context.GetSystemService(Context.ActivityService) is ActivityManager activity)
        {
            var memory = new ActivityManager.MemoryInfo();
            activity.GetMemoryInfo(memory);
            d["ram.totalMiB"] = (memory.TotalMem / (1024 * 1024)).ToString(CultureInfo.InvariantCulture);
            d["ram.lowRamDevice"] = activity.IsLowRamDevice.ToString();
            d["ram.memoryClassMiB"] = activity.MemoryClass.ToString(CultureInfo.InvariantCulture);
        }

        foreach (var m in typeof(EnvironmentInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (m.Key.StartsWith("Spike.", StringComparison.Ordinal))
            {
                d["build." + m.Key["Spike.".Length..]] = m.Value ?? string.Empty;
            }
        }
        return d;
    }
}
