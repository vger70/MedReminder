using System.Globalization;

namespace MedReminder.MobileSpikes.Spikes;

// Tab-separated logs for the spikes that span hours and reboots (S5,
// S8): written by receivers and workers that may run in a process the
// UI never started, read back by "Collect S5 and S8 results". Plain
// text, so no serializer is involved; synthetic values only.
internal static class SpikeFiles
{
    private static readonly Lock Gate = new();

    private static string Directory
    {
        get
        {
            var dir = Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "spikes");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static void Append(string file, params object?[] fields)
    {
        var line = string.Join('\t', fields.Select(f => Convert.ToString(f, CultureInfo.InvariantCulture)?.Replace('\t', ' ') ?? string.Empty));
        lock (Gate)
        {
            File.AppendAllText(Path.Combine(Directory, file), line + "\n");
        }
    }

    public static IReadOnlyList<string[]> Read(string file)
    {
        var path = Path.Combine(Directory, file);
        lock (Gate)
        {
            return File.Exists(path)
                ? [.. File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => l.Split('\t'))]
                : [];
        }
    }

    public static void Delete(string file)
    {
        lock (Gate)
        {
            File.Delete(Path.Combine(Directory, file));
        }
    }

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static string Local(long utcMs)
        => DateTimeOffset.FromUnixTimeMilliseconds(utcMs).ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
