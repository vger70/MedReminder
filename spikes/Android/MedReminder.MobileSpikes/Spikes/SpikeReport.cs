using System.Globalization;
using System.Text.Json;

namespace MedReminder.MobileSpikes.Spikes;

public enum Outcome
{
    Pass,
    Fail,
    Measured,
    Skipped,
}

public sealed record SpikeCheck(string Spike, string Check, Outcome Outcome, string Detail, double? ElapsedMs);

// Collects the results of one app session. The JSON is written with
// Utf8JsonWriter, not the reflection serializer: the report must not
// depend on the serializer mode that S3 measures. It carries device and
// build facts and synthetic or aggregate values only: no passphrase, no
// archive content beyond counts (CLAUDE.md §7).
public sealed class SpikeReport
{
    private readonly List<SpikeCheck> _checks = [];
    private readonly Lock _gate = new();

    public event Action<SpikeCheck>? Added;

    public void Add(string spike, string check, Outcome outcome, string detail, double? elapsedMs = null)
    {
        var entry = new SpikeCheck(spike, check, outcome, detail, elapsedMs);
        lock (_gate)
        {
            _checks.Add(entry);
        }
        Added?.Invoke(entry);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _checks.Clear();
        }
    }

    public IReadOnlyList<SpikeCheck> Snapshot()
    {
        lock (_gate)
        {
            return [.. _checks];
        }
    }

    public byte[] ToJson(IReadOnlyDictionary<string, string> environment)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("report", "medreminder-b1-android-spikes");
            json.WriteNumber("reportVersion", 1);
            json.WriteString("createdAtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

            json.WriteStartObject("environment");
            foreach (var (key, value) in environment)
            {
                json.WriteString(key, value);
            }
            json.WriteEndObject();

            json.WriteStartArray("checks");
            foreach (var c in Snapshot())
            {
                json.WriteStartObject();
                json.WriteString("spike", c.Spike);
                json.WriteString("check", c.Check);
                json.WriteString("outcome", c.Outcome.ToString());
                json.WriteString("detail", c.Detail);
                if (c.ElapsedMs is { } ms)
                {
                    json.WriteNumber("elapsedMs", Math.Round(ms, 1));
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return buffer.ToArray();
    }

    public static string Format(SpikeCheck c)
    {
        var elapsed = c.ElapsedMs is { } ms
            ? string.Create(CultureInfo.InvariantCulture, $" ({ms:0} ms)")
            : string.Empty;
        return $"[{c.Outcome}] {c.Spike} {c.Check}{elapsed}: {c.Detail}";
    }
}

// Runs one check: times it, and turns an exception into a Fail with its
// type and message (crypto, EF Core and JSON messages carry no secrets;
// S3 writes synthetic data only).
public static class SpikeRunner
{
    public static void Check(SpikeReport report, string spike, string check, Func<(bool Ok, string Detail)> body)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (ok, detail) = body();
            report.Add(spike, check, ok ? Outcome.Pass : Outcome.Fail, detail, watch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            report.Add(spike, check, Outcome.Fail, Describe(ex), watch.Elapsed.TotalMilliseconds);
        }
    }

    public static async Task CheckAsync(SpikeReport report, string spike, string check, Func<Task<(bool Ok, string Detail)>> body)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (ok, detail) = await body().ConfigureAwait(false);
            report.Add(spike, check, ok ? Outcome.Pass : Outcome.Fail, detail, watch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            report.Add(spike, check, Outcome.Fail, Describe(ex), watch.Elapsed.TotalMilliseconds);
        }
    }

    public static string Describe(Exception ex)
    {
        var text = $"{ex.GetType().FullName}: {ex.Message}";
        return ex.InnerException is { } inner
            ? $"{text} ---> {inner.GetType().FullName}: {inner.Message}"
            : text;
    }
}
