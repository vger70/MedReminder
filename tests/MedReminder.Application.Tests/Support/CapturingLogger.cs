using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Tests.Support;

// Records every log entry: level, rendered message, the structured
// state values and the attached exception, so a test can assert that
// no sensitive value reaches the logger through any of them.
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<Entry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.Select(p => $"{p.Key}={p.Value}").ToList()
            : new List<string>();
        Entries.Add(new Entry(logLevel, formatter(state, exception), values, exception));
    }

    // Everything a log sink could write for this entry.
    public IEnumerable<string> AllText() => Entries.SelectMany(e =>
        new[] { e.Message, e.Exception?.ToString() ?? string.Empty }.Concat(e.StateValues));

    internal sealed record Entry(
        LogLevel Level, string Message, IReadOnlyList<string> StateValues, Exception? Exception);
}
